using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class EnhancedBlobChangeToken : IChangeToken, IDisposable
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly string _blobPath;
    private readonly TimeSpan _debounceDelay;
    private readonly bool _useContentBasedDetection;
    private readonly int _maxContentHashSizeMb;
    private readonly ConcurrentDictionary<string, string> _contentHashes;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _cts;
    private volatile bool _hasChanged;
    private readonly object _lock = new object();
    private readonly List<(Action<object?> callback, object? state)> _callbacks = new();

    public bool HasChanged => _hasChanged;
    public bool ActiveChangeCallbacks => true;

    public EnhancedBlobChangeToken(
        BlobServiceClient blobServiceClient,
        string containerName,
        string blobPath,
        TimeSpan debounceDelay,
        bool useContentBasedDetection,
        int maxContentHashSizeMb,
        ConcurrentDictionary<string, string> contentHashes,
        ConcurrentDictionary<string, Timer> debounceTimers,
        ILogger logger)
    {
        _blobServiceClient = blobServiceClient;
        _containerName = containerName;
        _blobPath = blobPath;
        _debounceDelay = debounceDelay;
        _useContentBasedDetection = useContentBasedDetection;
        _maxContentHashSizeMb = maxContentHashSizeMb;
        _contentHashes = contentHashes;
        _debounceTimers = debounceTimers;
        _logger = logger;
        _cts = new CancellationTokenSource();

        StartWatching();
    }

    private void StartWatching()
    {
        _ = Task.Run(async () =>
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

                    await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while watching blob {BlobPath}", _blobPath);
                    await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token);
                }
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

            if (_useContentBasedDetection)
            {
                return await CheckContentHash(blobClient);
            }

            return await CheckETag(blobClient);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check blob changes for {BlobPath}", _blobPath);
            return false;
        }
    }

    private async Task<bool> CheckContentHash(BlobClient blobClient)
    {
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: _cts.Token);
        
        // Skip large files for content hashing
        if (properties.Value.ContentLength > _maxContentHashSizeMb * 1024 * 1024)
        {
            _logger.LogDebug("Blob {BlobPath} too large for content hashing, using ETag", _blobPath);
            return await CheckETag(blobClient);
        }

        await using var stream = await blobClient.OpenReadAsync(cancellationToken: _cts.Token);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, _cts.Token);
        var currentHash = Convert.ToBase64String(hashBytes);

        var previousHash = _contentHashes.GetValueOrDefault(_blobPath);
        if (currentHash != previousHash)
        {
            _contentHashes[_blobPath] = currentHash;
            _logger.LogInformation("Content change detected for blob {BlobPath}. Hash changed from {OldHash} to {NewHash}",
                _blobPath, previousHash?.Substring(0, 8) + "...", currentHash.Substring(0, 8) + "...");
            return true;
        }

        return false;
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
                existingTimer?.Dispose();
            }

            // Create new debounce timer
            var timer = new Timer(_ =>
            {
                _hasChanged = true;
                NotifyCallbacks();
                _logger.LogInformation("Debounced change notification triggered for blob {BlobPath} after {Delay}s delay",
                    _blobPath, _debounceDelay.TotalSeconds);
            }, null, _debounceDelay, Timeout.InfiniteTimeSpan);

            _debounceTimers[_blobPath] = timer;
            
            _logger.LogDebug("Change detected for blob {BlobPath}, starting {Delay}s debounce timer",
                _blobPath, _debounceDelay.TotalSeconds);
        }
    }

    private void NotifyCallbacks()
    {
        lock (_lock)
        {
            foreach (var (callback, state) in _callbacks)
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
    }

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        lock (_lock)
        {
            _callbacks.Add((callback, state));
            return new CallbackRegistration(() =>
            {
                lock (_lock)
                {
                    _callbacks.Remove((callback, state));
                }
            });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        if (_debounceTimers.TryRemove(_blobPath, out var timer))
        {
            timer.Dispose();
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
