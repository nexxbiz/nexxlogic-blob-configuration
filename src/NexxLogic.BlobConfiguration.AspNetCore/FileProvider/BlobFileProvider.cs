using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using System.Collections.Concurrent;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobFileProvider : IFileProvider, IDisposable
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobFileProvider> _logger;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly TimeSpan _debounceDelay;
    private readonly bool _useContentBasedDetection;
    private readonly int _maxContentHashSizeMb;
    private readonly bool _enableDetailedLogging;
    private readonly ConcurrentDictionary<string, string> _contentHashes;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers;
    private volatile bool _disposed;

    private BlobChangeToken _changeToken = new();
    /// <summary>
    /// The timestamp in ticks when the blob was last modified.
    /// </summary>
    private long _lastModified;

    /// <summary>
    /// True on initial load and when a change has been raised but not retrieved.
    /// </summary>
    private bool _loadPending = true;

    /// <summary>
    /// Whether the blob exists. The watch should stop when it does not exist.
    /// </summary>
    private bool _exists;

    public BlobFileProvider(IBlobClientFactory blobClientFactory,
        IBlobContainerClientFactory blobContainerClientFactory,
        BlobConfigurationOptions blobConfig,
        ILogger<BlobFileProvider> logger)
    {
        _blobClientFactory = blobClientFactory;
        _blobConfig = blobConfig;
        _blobContainerClientFactory = blobContainerClientFactory;
        _logger = logger;

        // Initialize enhanced options with defaults
        _debounceDelay = TimeSpan.FromSeconds(_blobConfig.DebounceDelaySeconds);
        _useContentBasedDetection = _blobConfig.UseContentBasedChangeDetection;
        _maxContentHashSizeMb = _blobConfig.MaxFileContentHashSizeMb;
        _enableDetailedLogging = _blobConfig.EnableDetailedLogging;
        
        _contentHashes = new ConcurrentDictionary<string, string>();
        _debounceTimers = new ConcurrentDictionary<string, Timer>();

        // Create BlobServiceClient for enhanced change token if enhanced features are used
        if (_useContentBasedDetection)
        {
            try
            {
                if (!string.IsNullOrEmpty(_blobConfig.ConnectionString))
                {
                    _blobServiceClient = new BlobServiceClient(_blobConfig.ConnectionString);
                }
                else if (!string.IsNullOrEmpty(_blobConfig.BlobContainerUrl))
                {
                    var containerUri = new Uri(_blobConfig.BlobContainerUrl);
                    var serviceUri = new Uri($"{containerUri.Scheme}://{containerUri.Host}");
                    _blobServiceClient = new BlobServiceClient(serviceUri);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create BlobServiceClient for enhanced features. Falling back to legacy mode.");
            }
        }

        if (_enableDetailedLogging)
        {
            _logger.LogInformation("BlobFileProvider initialized with debounce: {Debounce}s, content-based detection: {ContentBased}",
                _debounceDelay.TotalSeconds, _useContentBasedDetection);
        }
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var blobClient = _blobClientFactory.GetBlobClient(subpath);
        var result = new BlobFileInfo(blobClient);

        _lastModified = result.LastModified.Ticks;
        _loadPending = false;
        _exists = result.Exists;

        return result;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var containerClient = _blobContainerClientFactory.GetBlobContainerClient();
        var fileInfos = new List<IFileInfo>();

        foreach (var blobInfoPage in containerClient.GetBlobs(prefix: _blobConfig.Prefix).AsPages())
        {
            foreach (var blobInfo in blobInfoPage.Values)
            {
                var blobClient = containerClient.GetBlobClient(blobInfo.Name);
                var blob = new BlobFileInfo(blobClient);
                _lastModified = Math.Max(blob.LastModified.Ticks, _lastModified);
                fileInfos.Add(blob);
            }
        }
        _loadPending = false;

        // If the code runs until here, the container will always exist, otherwise it would have 
        // thrown an error when getting the blobs. Previously, the container.Exists() function was called
        // which would always return true here, but because of that, we would always need an Account SAS token,
        // or a connection string, granting access to all the storage account, which is not always intended and 
        // not necessary
        _exists = true;
        var blobDirectoryContents = new BlobDirectoryContents(_exists, fileInfos);

        return blobDirectoryContents;
    }

    public IChangeToken Watch(string filter)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlobFileProvider));

        // Use enhanced change token if available, otherwise fall back to legacy implementation
        if (_blobServiceClient != null && _useContentBasedDetection)
        {
            if (_enableDetailedLogging)
            {
                _logger.LogDebug("Creating enhanced watch token for filter: {Filter} with debounce: {Debounce}s", 
                    filter, _debounceDelay.TotalSeconds);
            }

            var blobPath = GetBlobPath(filter);
            
            return new EnhancedBlobChangeToken(
                _blobServiceClient,
                _blobConfig.ContainerName,
                blobPath,
                _debounceDelay,
                _useContentBasedDetection,
                _maxContentHashSizeMb,
                _contentHashes,
                _debounceTimers,
                _logger);
        }

        // Legacy implementation
        var client = _blobClientFactory.GetBlobClient(filter);
        _ = WatchBlobUpdate(client, _changeToken.CancellationToken);
        return _changeToken;
    }

    private string GetBlobPath(string filter)
    {
        // Remove leading slash if present
        filter = filter.TrimStart('/');
        
        // Combine with prefix if configured
        if (!string.IsNullOrEmpty(_blobConfig.Prefix))
        {
            return $"{_blobConfig.Prefix.TrimEnd('/')}/{filter}";
        }
        
        return filter;
    }

    private async Task WatchBlobUpdate(BlobClient blobClient, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_blobConfig.ReloadInterval, token);
                _logger.LogInformation("check change blob settings");

                if (_loadPending)
                {
                    _logger.LogWarning("load pending");
                    continue;
                }

                if (!_exists)
                {
                    _logger.LogWarning("file does not exist");

                    if (_blobConfig.Optional)
                    {
                        _logger.LogInformation("Checking if optional file is added to blob container since last scan");

                        var doesItExistNow = blobClient.Exists(cancellationToken: token);
                        if (doesItExistNow)
                        {
                            RaiseChanged();
                        }
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                var properties = await blobClient.GetPropertiesAsync(cancellationToken: token);
                if (properties.Value.LastModified.Ticks > _lastModified)
                {
                    _logger.LogWarning("change raised");
                    RaiseChanged();
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("task cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"error occured {ex.Message}");
                // If an exception is not caught, then it will stop the watch loop. This will retry at the next interval.
                // Additional error handling can be implemented in the future, like:
                // - Max retries
                // - Exponential backoff
                // - Logging
            }
        }
    }

    private void RaiseChanged()
    {
        var previousToken = Interlocked.Exchange(ref _changeToken, new BlobChangeToken());
        _loadPending = true;
        previousToken.OnReload();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose all active debounce timers
        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }
        _debounceTimers.Clear();

        if (_enableDetailedLogging)
        {
            _logger.LogDebug("BlobFileProvider disposed");
        }
    }
}