using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// Enhanced change token that monitors blob changes with optimized change detection strategies.
/// Implements both IDisposable and IAsyncDisposable.
/// 
/// IMPORTANT: For proper resource cleanup, always use 'await using' or call DisposeAsync() directly.
/// The synchronous Dispose() method is provided for compatibility but may not properly clean up
/// background tasks in high-throughput scenarios.
/// 
/// Example:
/// <code>
/// await using var token = new EnhancedBlobChangeToken(...);
/// // or
/// var token = new EnhancedBlobChangeToken(...);
/// try { /* use token */ }
/// finally { await token.DisposeAsync(); }
/// </code>
/// </summary>
internal class EnhancedBlobChangeToken : IChangeToken, IDisposable, IAsyncDisposable
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly string _blobPath;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _watchingInterval;
    private readonly TimeSpan _errorRetryDelay;
    private readonly IChangeDetectionStrategy _changeDetectionStrategy;
    private readonly ConcurrentDictionary<string, string> _blobFingerprints;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _cts;
    private readonly Task _watchingTask;
    private Timer? _debounceTimer;
    private volatile bool _hasChanged;
    private int _disposed; // 0 = false, 1 = true (used with Interlocked for thread-safety)
    private readonly object _lock = new object();
    private readonly Dictionary<Guid, (Action<object?> callback, object? state)> _callbacks = new();

    public bool HasChanged => _hasChanged;
    
    /// <summary>
    /// Gets a value indicating whether there are any active change callbacks registered.
    /// Returns true if the token is not disposed AND has callbacks registered.
    /// </summary>
    public bool ActiveChangeCallbacks 
    {
        get
        {
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
                return false;
                
            lock (_lock)
            {
                return _callbacks.Count > 0;
            }
        }
    }

    public EnhancedBlobChangeToken(
        BlobServiceClient blobServiceClient,
        string containerName,
        string blobPath,
        TimeSpan debounceDelay,
        TimeSpan watchingInterval,
        TimeSpan errorRetryDelay,
        IChangeDetectionStrategy changeDetectionStrategy,
        ConcurrentDictionary<string, string> blobFingerprints,
        ILogger logger)
    {
        _blobServiceClient = blobServiceClient;
        _containerName = containerName;
        _blobPath = blobPath;
        _debounceDelay = debounceDelay;
        _watchingInterval = watchingInterval;
        _errorRetryDelay = errorRetryDelay;
        _changeDetectionStrategy = changeDetectionStrategy;
        _blobFingerprints = blobFingerprints;
        _logger = logger;
        _cts = new CancellationTokenSource();

        _watchingTask = StartWatching();
        
        // Ensure unobserved task exceptions are logged
        _watchingTask.ContinueWith(task =>
        {
            if (task is { IsFaulted: true, Exception: not null })
            {
                _logger.LogError(task.Exception, "Unobserved exception in watching task for blob {BlobPath}", _blobPath);
            }
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private Task StartWatching()
    {
        return Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var hasContentChanged = await CheckForContentChanges();
                        if (hasContentChanged)
                        {
                            TriggerDebouncedChange();
                        }

                        await Task.Delay(_watchingInterval, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error while watching blob {BlobPath}", _blobPath);
                        
                        try
                        {
                            await Task.Delay(_errorRetryDelay, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when cancellation is requested during retry delay
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in blob watching task for {BlobPath}", _blobPath);
            }
        }, _cts.Token);
    }

    private async Task<bool> CheckForContentChanges()
    {
        // Early exit if disposed to avoid unnecessary work
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0 || _cts.Token.IsCancellationRequested)
        {
            return false;
        }
        
        try
        {
            var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName)
                .GetBlobClient(_blobPath);

            var exists = await blobClient.ExistsAsync(_cts.Token);
            if (!exists.Value)
            {
                _logger.LogDebug("Blob {BlobPath} does not exist", _blobPath);
                return false;
            }

            // Get blob properties once and pass them to the strategy via context
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: _cts.Token);
            
            var context = new ChangeDetectionContext
            {
                BlobClient = blobClient,
                BlobPath = _blobPath,
                Properties = properties.Value,
                BlobFingerprints = _blobFingerprints,
                CancellationToken = _cts.Token
            };

            var hasChanged = await _changeDetectionStrategy.HasChangedAsync(context);
            return hasChanged;
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal - don't log as warning
            return false;
        }
        catch (Exception ex)
        {
            // Only log if we're not disposed - errors during disposal are expected
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0 && !_cts.Token.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to check blob changes for {BlobPath}", _blobPath);
            }
            return false;
        }
    }

    private void TriggerDebouncedChange()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return; // Early exit if already disposed
        
        lock (_lock)
        {
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return; // Double-check after acquiring lock
            
            // Create new debounce timer first to avoid race condition window
            var newTimer = new Timer(_ =>
            {
                try
                {
                    // Check disposal state
                    if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
                    {
                        return;
                    }

                    // Execute the change notification
                    _hasChanged = true;
                    NotifyCallbacks();
                    _logger.LogInformation("Debounced change notification triggered for blob {BlobPath} after {Delay}s delay",
                        _blobPath, _debounceDelay.TotalSeconds);
                }
                catch (Exception ex)
                {
                    // Only log errors if we're not disposed
                    if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
                    {
                        _logger.LogError(ex, "Error executing debounced change callback for blob {BlobPath}", _blobPath);
                    }
                }
            }, null, _debounceDelay, Timeout.InfiniteTimeSpan);
            
            // Atomically replace and dispose old timer after creating new one
            var oldTimer = Interlocked.Exchange(ref _debounceTimer, newTimer);
            oldTimer?.Dispose();
            
            
            _logger.LogDebug("Change detected for blob {BlobPath}, starting {Delay}s debounce timer",
                _blobPath, _debounceDelay.TotalSeconds);
        }
    }

    private void NotifyCallbacks()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return; // Guard against disposal
        
        // Capture callbacks inside the lock to avoid modification during iteration
        (Action<object?> callback, object? state)[] callbacksSnapshot;
        
        lock (_lock)
        {
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return; // Double-check after acquiring lock
            
            // Take a snapshot of callbacks to execute outside the lock
            callbacksSnapshot = _callbacks.Values.ToArray();
        }
        
        // Execute callbacks outside the lock to prevent deadlocks
        foreach (var (callback, state) in callbacksSnapshot)
        {
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing change callback for blob {BlobPath}", _blobPath);
            }
        }
    }

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) throw new ObjectDisposedException(nameof(EnhancedBlobChangeToken));

        bool invokeImmediately;
        Guid callbackId;

        lock (_lock)
        {
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) throw new ObjectDisposedException(nameof(EnhancedBlobChangeToken));
            invokeImmediately = _hasChanged;

            callbackId = Guid.NewGuid();
            _callbacks.Add(callbackId, (callback, state));
        }

        if (invokeImmediately)
        {
            // invoke outside lock
            callback(state);
        }

        return new CallbackRegistration(() =>
        {
            lock (_lock)
            {
                _callbacks.Remove(callbackId);
            }
        });
    }
    /// <summary>
    /// Asynchronously disposes the EnhancedBlobChangeToken with proper cleanup of all resources.
    /// This is the PREFERRED disposal method as it:
    /// - Properly cancels and waits for the background watching task to complete
    /// - Ensures all resources (timers, cancellation tokens) are fully disposed
    /// - Prevents unobserved task exceptions
    /// - Is safe to use in high-throughput scenarios
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation</returns>
    public async ValueTask DisposeAsync()
    {
        // Use Interlocked.CompareExchange to atomically check and set disposal flag
        // This prevents race conditions where multiple threads could both proceed with disposal
        // Returns the original value - if it was already 1 (disposed), we return early
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }
        
        try
        {
            await _cts.CancelAsync();
            
            try
            {
                await _watchingTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // Expected - task was cancelled
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout while waiting for watching task to complete for blob {BlobPath}", _blobPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while waiting for watching task to complete for blob {BlobPath}", _blobPath);
            }
            
            lock (_lock)
            {
                _callbacks.Clear();
            }
            
            // Properly await timer async disposal
            // The disposal flag was already set atomically at the beginning of this method,
            // so any pending timer callbacks will see _disposed == 1 and exit early without executing
            var timerTask = _debounceTimer?.DisposeAsync() ?? ValueTask.CompletedTask;
            await timerTask;
            
            _cts.Dispose();
            
            _logger.LogDebug("EnhancedBlobChangeToken disposed asynchronously for blob {BlobPath}", _blobPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async disposal of EnhancedBlobChangeToken for blob {BlobPath}", _blobPath);
        }
    }

    /// <summary>
    /// Synchronously disposes the EnhancedBlobChangeToken.
    /// WARNING: This method does not wait for the background watching task to complete,
    /// which may result in unobserved task exceptions or incomplete cleanup.
    /// For proper resource cleanup, prefer <see cref="DisposeAsync"/> when possible.
    /// This method exists primarily to satisfy the IDisposable contract for callers
    /// that cannot use asynchronous disposal.
    /// </summary>
    public void Dispose()
    {
        // Synchronous disposal for IDisposable compatibility
        // This should ideally not be used in high-throughput scenarios
        // Prefer DisposeAsync() when possible
        
        // Use Interlocked.CompareExchange to atomically check and set disposal flag
        // This prevents race conditions where multiple threads could both proceed with disposal
        // Returns the original value - if it was already 1 (disposed), we return early
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        try
        {
            // For synchronous disposal, we don't block on task completion
            // Just signal cancellation and dispose resources immediately
            _cts.Cancel();
            
            lock (_lock)
            {
                _callbacks.Clear();
            }
            
            // Dispose the debounce timer if it exists
            _debounceTimer?.Dispose();
            
            _cts.Dispose();
            
            _logger.LogDebug("EnhancedBlobChangeToken disposed synchronously for blob {BlobPath} (task may still be running)", _blobPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during synchronous disposal of EnhancedBlobChangeToken for blob {BlobPath}", _blobPath);
        }
    }

    private class CallbackRegistration(Action unregister) : IDisposable
    {
        public void Dispose()
        {
            unregister();
        }
    }
}