using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class EnhancedBlobChangeToken : IChangeToken, IDisposable
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly string _blobPath;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _watchingInterval;
    private readonly TimeSpan _errorRetryDelay;
    private readonly IChangeDetectionStrategy _changeDetectionStrategy;
    private readonly ConcurrentDictionary<string, string> _contentHashes;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _cts;
    private readonly Task _watchingTask;
    private volatile bool _hasChanged;
    private volatile bool _disposed;
    private readonly object _lock = new object();
    private readonly Dictionary<Guid, (Action<object?> callback, object? state)> _callbacks = new();

    public bool HasChanged => _hasChanged;
    public bool ActiveChangeCallbacks => true;

    public EnhancedBlobChangeToken(
        BlobServiceClient blobServiceClient,
        string containerName,
        string blobPath,
        TimeSpan debounceDelay,
        TimeSpan watchingInterval,
        TimeSpan errorRetryDelay,
        IChangeDetectionStrategy changeDetectionStrategy,
        ConcurrentDictionary<string, string> contentHashes,
        ConcurrentDictionary<string, Timer> debounceTimers,
        ILogger logger)
    {
        _blobServiceClient = blobServiceClient;
        _containerName = containerName;
        _blobPath = blobPath;
        _debounceDelay = debounceDelay;
        _watchingInterval = watchingInterval;
        _errorRetryDelay = errorRetryDelay;
        _changeDetectionStrategy = changeDetectionStrategy;
        _contentHashes = contentHashes;
        _debounceTimers = debounceTimers;
        _logger = logger;
        _cts = new CancellationTokenSource();

        _watchingTask = StartWatching();
        
        // Ensure unobserved task exceptions are logged
        _watchingTask.ContinueWith(task =>
        {
            if (task.IsFaulted && task.Exception != null)
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

            return await _changeDetectionStrategy.HasChangedAsync(blobClient, _blobPath, _contentHashes, _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check blob changes for {BlobPath}", _blobPath);
            return false;
        }
    }

    private async Task<bool> CheckETag(BlobClient blobClient)
    {
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: _cts.Token);
        var currentETag = properties.Value.ETag.ToString();

        var previousETag = _contentHashes.GetValueOrDefault($"{_blobPath}:etag");
        if (currentETag != previousETag)
        {
            _contentHashes[$"{_blobPath}:etag"] = currentETag;
            _logger.LogInformation("ETag change detected for blob {BlobPath}. ETag changed from {OldETag} to {NewETag}",
                _blobPath, previousETag, currentETag);
            return true;
        }

        return false;
    }

    private void TriggerDebouncedChange()
    {
        lock (_lock)
        {
            // Cancel existing timer
            if (_debounceTimers.TryRemove(_blobPath, out var existingTimer))
            {
                existingTimer.Dispose();
            }

            // Create new debounce timer
            var timer = new Timer(_ =>
            {
                if (!_disposed)
                {
                    _hasChanged = true;
                    NotifyCallbacks();
                    _logger.LogInformation("Debounced change notification triggered for blob {BlobPath} after {Delay}s delay",
                        _blobPath, _debounceDelay.TotalSeconds);
                }
            }, null, _debounceDelay, Timeout.InfiniteTimeSpan);

            _debounceTimers[_blobPath] = timer;
            
            _logger.LogDebug("Change detected for blob {BlobPath}, starting {Delay}s debounce timer",
                _blobPath, _debounceDelay.TotalSeconds);
        }
    }

    private void NotifyCallbacks()
    {
        if (_disposed) return; // Guard against disposal
        
        lock (_lock)
        {
            if (_disposed) return; // Double-check after acquiring lock
            
            foreach (var kvp in _callbacks)
            {
                var (callback, state) = kvp.Value;
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
    }

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EnhancedBlobChangeToken));
        
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EnhancedBlobChangeToken));
            
            var callbackId = Guid.NewGuid();
            _callbacks.Add(callbackId, (callback, state));
            
            return new CallbackRegistration(() =>
            {
                lock (_lock)
                {
                    _callbacks.Remove(callbackId);
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return; // Already disposed
        
        try
        {
            // Step 1: Set disposed flag to prevent new operations
            _disposed = true;
            
            // Step 2: Cancel the token to signal background task to stop
            _cts.Cancel();

            // Step 3: Wait for the background task to complete (with timeout to prevent hanging)
            try
            {
                _watchingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Expected - task was cancelled
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while waiting for watching task to complete for blob {BlobPath}", _blobPath);
            }

            // Step 4: Clear all callbacks to prevent further notifications
            lock (_lock)
            {
                _callbacks.Clear();
            }

            // Step 5: Dispose resources (timers)
            if (_debounceTimers.TryRemove(_blobPath, out var timer))
            {
                timer.Dispose();
            }

            // Step 6: Dispose the CancellationTokenSource
            _cts.Dispose();
            
            _logger.LogDebug("EnhancedBlobChangeToken disposed for blob {BlobPath}", _blobPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal of EnhancedBlobChangeToken for blob {BlobPath}", _blobPath);
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
