using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobFileProvider : IFileProvider, IDisposable
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobFileProvider> _logger;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _watchingInterval;
    private readonly TimeSpan _errorRetryDelay;
    private readonly IChangeDetectionStrategyFactory _strategyFactory;
    private readonly IChangeDetectionStrategy _changeDetectionStrategyInstance;
    private readonly int _maxContentHashSizeMb;
    private readonly ConcurrentDictionary<string, string> _blobFingerprints;
    private readonly ConcurrentDictionary<string, WeakReference<EnhancedBlobChangeToken>> _tokenCache;
    private readonly object _tokenCreationLock = new object();
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

        // Validate configuration values at runtime
        ValidateConfiguration(blobConfig);

        // Initialize enhanced options with validated values
        _debounceDelay = TimeSpan.FromSeconds(_blobConfig.DebounceDelaySeconds);
        _watchingInterval = TimeSpan.FromSeconds(_blobConfig.WatchingIntervalSeconds);
        _errorRetryDelay = TimeSpan.FromSeconds(_blobConfig.ErrorRetryDelaySeconds);
        _strategyFactory = new ChangeDetectionStrategyFactory(_blobConfig);
        _maxContentHashSizeMb = _blobConfig.MaxFileContentHashSizeMb;
        
        // Create the strategy instance once and reuse it
        _changeDetectionStrategyInstance = CreateChangeDetectionStrategy();
        
        _blobFingerprints = new ConcurrentDictionary<string, string>();
        _tokenCache = new ConcurrentDictionary<string, WeakReference<EnhancedBlobChangeToken>>();
        
        try
        {
            if (!string.IsNullOrEmpty(_blobConfig.ConnectionString))
            {
                _blobServiceClient = new BlobServiceClient(_blobConfig.ConnectionString);
            }
            else if (!string.IsNullOrEmpty(_blobConfig.BlobContainerUrl))
            {
                var containerUri = new Uri(_blobConfig.BlobContainerUrl);

                // Only create a BlobServiceClient when the container URL does not contain a SAS token (query string).
                // If a SAS is present, using only scheme and host would create an unauthenticated client and break enhanced features.
                if (string.IsNullOrEmpty(containerUri.Query))
                {
                    var serviceUri = new Uri($"{containerUri.Scheme}://{containerUri.Host}");
                    _blobServiceClient = new BlobServiceClient(serviceUri);
                }
                else
                {
                    _logger.LogInformation(
                        "BlobContainerUrl contains a SAS token; skipping BlobServiceClient creation for enhanced features. Falling back to legacy mode.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create BlobServiceClient for enhanced features. Falling back to legacy mode.");
        }

        _logger.LogDebug("BlobFileProvider initialized with debounce: {Debounce}s, strategy: {Strategy}",
            _debounceDelay.TotalSeconds, _strategyFactory.GetType().Name);
    }

    private static void ValidateConfiguration(BlobConfigurationOptions config)
    {
        // Use DataAnnotations validation to enforce Range attributes automatically
        var validationContext = new ValidationContext(config);
        var validationResults = new List<ValidationResult>();
        
        bool isValid = Validator.TryValidateObject(config, validationContext, validationResults, validateAllProperties: true);
        
        if (!isValid)
        {
            var errorMessages = validationResults
                .Where(r => !string.IsNullOrEmpty(r.ErrorMessage))
                .Select(r => r.ErrorMessage!)
                .ToArray();
            
            var message = "Invalid BlobConfiguration values:" + Environment.NewLine + string.Join(Environment.NewLine, errorMessages);
            throw new ArgumentException(message, nameof(config));
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BlobFileProvider));
        }

        // Use enhanced change token if available, otherwise fall back to legacy implementation
        if (_blobServiceClient != null)
        {
            var blobPath = GetBlobPath(filter);
            
            // Check cache first and reuse existing token if still alive
            if (_tokenCache.TryGetValue(blobPath, out var weakRef) && weakRef.TryGetTarget(out var existingToken))
            {
                _logger.LogDebug("Reusing existing enhanced watch token for filter: {Filter}", filter);
                return existingToken;
            }
            
            // Use lock to prevent race condition when creating new tokens
            lock (_tokenCreationLock)
            {
                // Double-check pattern: verify token wasn't created by another thread
                if (_tokenCache.TryGetValue(blobPath, out weakRef) && weakRef.TryGetTarget(out existingToken))
                {
                    _logger.LogDebug("Found existing enhanced watch token created by another thread for filter: {Filter}", filter);
                    return existingToken;
                }
                
                _logger.LogDebug("Creating new enhanced watch token for filter: {Filter} with strategy: {Strategy}", 
                    filter, _strategyFactory.GetType().Name);
                
                var newToken = new EnhancedBlobChangeToken(
                    _blobServiceClient,
                    _blobConfig.ContainerName,
                    blobPath,
                    _debounceDelay,
                    _watchingInterval,
                    _errorRetryDelay,
                    _changeDetectionStrategyInstance,
                    _blobFingerprints,
                    _logger);
                
                // Cache the new token using WeakReference to avoid memory leaks
                _tokenCache[blobPath] = new WeakReference<EnhancedBlobChangeToken>(newToken);
                
                // Clean up dead references periodically (simple cleanup strategy)
                if (_tokenCache.Count > 100) // Arbitrary threshold
                {
                    CleanupDeadReferences();
                }
                
                return newToken;
            }
        }

        // Legacy implementation
        var client = _blobClientFactory.GetBlobClient(filter);
        _ = WatchBlobUpdate(client, _changeToken.CancellationToken);
        return _changeToken;
    }

    private IChangeDetectionStrategy CreateChangeDetectionStrategy()
    {
        return _strategyFactory.CreateStrategy(_logger, _maxContentHashSizeMb);
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
        
        // Dispose the previous token to clean up its CancellationTokenSource
        try
        {
            previousToken.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing previous change token during reload");
        }
    }

    private void CleanupDeadReferences()
    {
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _tokenCache)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _tokenCache.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} dead token references", keysToRemove.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel and dispose legacy change token to stop WatchBlobUpdate tasks
        try
        {
            _changeToken.Dispose();
            _logger.LogDebug("Legacy change token cancelled and disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing legacy change token");
        }


        _logger.LogDebug("BlobFileProvider disposed");
        
        GC.SuppressFinalize(this);
    }
}