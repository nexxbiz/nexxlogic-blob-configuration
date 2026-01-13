using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;
using Azure.Identity;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// File provider that monitors Azure Blob Storage for file changes with enhanced change detection capabilities.
/// 
/// Authentication Requirements:
/// 
/// 1. ConnectionString: Provides full authentication - enables enhanced features
///    Example: "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=..."
/// 
/// 2. BlobContainerUrl with SAS token: Falls back to legacy mode (no enhanced features)
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer?sv=2020-08-04&amp;ss=b..."
///    Rationale: SAS tokens provide container-level access, but enhanced features need storage account-level access
/// 
/// 3. BlobContainerUrl without SAS token: Uses DefaultAzureCredential, enables enhanced features
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer"
///    Requires one of:
///    - Managed Identity (recommended for Azure-hosted applications)
///    - Azure CLI authentication: `az login` (for local development)
///    - Environment variables: AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID
///    - Visual Studio or VS Code authentication
///    - WorkloadIdentity (for AKS with federated identity)
/// 
/// If authentication fails, the provider falls back to legacy mode with reduced functionality.
/// </summary>
public class BlobFileProvider : IFileProvider, IDisposable
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobFileProvider> _logger;
    private readonly BlobServiceClient? _blobServiceClient;
    // Enhanced blob watching configuration - using TimeSpan for better expressiveness
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _watchingInterval;
    private readonly TimeSpan _errorRetryDelay;
    private readonly IChangeDetectionStrategyFactory _strategyFactory;
    private readonly int _maxContentHashSizeMb;
    private readonly ConcurrentDictionary<string, string> _blobFingerprints;
    private readonly ConcurrentDictionary<string, WeakReference<EnhancedBlobChangeToken>> _tokenCache;
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

        // Initialize enhanced options directly from TimeSpan properties
        _debounceDelay = _blobConfig.DebounceDelay;
        _watchingInterval = _blobConfig.WatchingInterval;
        _errorRetryDelay = _blobConfig.ErrorRetryDelay;
        _strategyFactory = new ChangeDetectionStrategyFactory(_blobConfig);
        _maxContentHashSizeMb = _blobConfig.MaxFileContentHashSizeMb;
        
        _blobFingerprints = new ConcurrentDictionary<string, string>();
        _tokenCache = new ConcurrentDictionary<string, WeakReference<EnhancedBlobChangeToken>>();
        
        try
        {
            if (!string.IsNullOrEmpty(_blobConfig.ConnectionString))
            {
                _blobServiceClient = new BlobServiceClient(_blobConfig.ConnectionString);
                _logger.LogDebug("BlobServiceClient created using connection string");
            }
            else if (!string.IsNullOrEmpty(_blobConfig.BlobContainerUrl))
            {
                var containerUri = new Uri(_blobConfig.BlobContainerUrl);

                // If a SAS token is present (query string), fallback to legacy mode
                // Enhanced features require either ConnectionString or BlobContainerUrl without SAS + DefaultAzureCredential
                if (!string.IsNullOrEmpty(containerUri.Query))
                {
                    _logger.LogInformation(
                        "BlobContainerUrl contains a SAS token; skipping BlobServiceClient creation for enhanced features. " +
                        "Falling back to legacy mode.");
                    // Don't create _blobServiceClient - this forces fallback to legacy mode
                }
                else
                {
                    // No SAS token present - use DefaultAzureCredential for authentication
                    // This requires one of the following to be configured in the environment:
                    // - Managed Identity (recommended for Azure-hosted applications)
                    // - Azure CLI authentication (for local development)
                    // - Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
                    // - Visual Studio or VS Code authentication
                    var serviceUri = new Uri($"{containerUri.Scheme}://{containerUri.Host}");
                    var credential = new DefaultAzureCredential();
                    
                    _blobServiceClient = new BlobServiceClient(serviceUri, credential);
                    _logger.LogInformation(
                        "BlobServiceClient created using DefaultAzureCredential. Ensure Azure credentials are configured " +
                        "(Managed Identity, Azure CLI, environment variables, or IDE authentication).");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to create BlobServiceClient for enhanced features. " +
                "If using BlobContainerUrl without SAS token, ensure Azure credentials are properly configured " +
                "(Managed Identity, Azure CLI, environment variables, or IDE authentication). " +
                "Falling back to legacy mode.");
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

        // Additional explicit validation for TimeSpan properties to ensure Range validation works correctly
        if (config.DebounceDelay < TimeSpan.Zero || config.DebounceDelay > TimeSpan.FromHours(1))
        {
            validationResults.Add(new ValidationResult("DebounceDelay must be between 0 seconds and 1 hour. Use 0 to disable debouncing.", new[] { nameof(config.DebounceDelay) }));
            isValid = false;
        }

        if (config.WatchingInterval < TimeSpan.FromSeconds(1) || config.WatchingInterval > TimeSpan.FromHours(24))
        {
            validationResults.Add(new ValidationResult("WatchingInterval must be between 1 second and 24 hours.", new[] { nameof(config.WatchingInterval) }));
            isValid = false;
        }

        if (config.ErrorRetryDelay < TimeSpan.FromSeconds(1) || config.ErrorRetryDelay > TimeSpan.FromHours(2))
        {
            validationResults.Add(new ValidationResult("ErrorRetryDelay must be between 1 second and 2 hours.", new[] { nameof(config.ErrorRetryDelay) }));
            isValid = false;
        }
        
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

            // Use GetOrAdd with factory function to atomically get existing or create new weak reference
            var weakRef = _tokenCache.GetOrAdd(blobPath, _ => CreateTokenWeakReference(blobPath, filter));

            // Check if the weak reference still has a live target
            if (weakRef.TryGetTarget(out var existingToken))
            {
                _logger.LogDebug("Reusing existing enhanced watch token for filter: {Filter}", filter);
                return existingToken;
            }

            // If the weak reference is dead, atomically update the cache and get the resulting weak reference.
            // AddOrUpdate ensures we see the latest value for the key and avoid racy TryUpdate logic.
            weakRef = _tokenCache.AddOrUpdate(
                blobPath,
                _ => CreateTokenWeakReference(blobPath, filter),
                (_, current) =>
                {
                    // If the current weak reference still has a live target, keep using it.
                    if (current.TryGetTarget(out var _))
                    {
                        return current;
                    }

                    // Otherwise, create a new weak reference and token.
                    return CreateTokenWeakReference(blobPath, filter);
                });

            // Try to get the token from the (possibly updated) weak reference.
            if (weakRef.TryGetTarget(out var newToken))
            {
                // Clean up dead references periodically (simple cleanup strategy)
                if (_tokenCache.Count > 100)
                {
                    CleanupDeadReferences();
                }

                return newToken;
            }

            // This should not happen; create a new token directly without attempting further cache recovery.
            _logger.LogWarning(
                "Failed to get token from weak reference; creating new token directly for filter: {Filter}",
                filter);
            return CreateEnhancedToken(blobPath, filter);
        }

        // Legacy implementation
        var client = _blobClientFactory.GetBlobClient(filter);
        _ = WatchBlobUpdate(client, _changeToken.CancellationToken);
        return _changeToken;
    }

    private IChangeDetectionStrategy CreateChangeDetectionStrategy()
    {
        return _strategyFactory.CreateStrategy(_logger);
    }

    private WeakReference<EnhancedBlobChangeToken> CreateTokenWeakReference(string blobPath, string filter)
    {
        var token = CreateEnhancedToken(blobPath, filter);
        return new WeakReference<EnhancedBlobChangeToken>(token);
    }

    private EnhancedBlobChangeToken CreateEnhancedToken(string blobPath, string filter)
    {
        _logger.LogDebug("Creating new enhanced watch token for filter: {Filter} with strategy: {Strategy}", 
            filter, _strategyFactory.GetType().Name);
        
        return new EnhancedBlobChangeToken(
            _blobServiceClient!,
            _blobConfig.ContainerName,
            blobPath,
            _debounceDelay,
            _watchingInterval,
            _errorRetryDelay,
            CreateChangeDetectionStrategy(),
            _blobFingerprints,
            _logger);
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
        var keysToRemove = _tokenCache
            .Where(kvp => !kvp.Value.TryGetTarget(out _))
            .Select(kvp => kvp.Key)
            .ToList();
        
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

        // Dispose all cached EnhancedBlobChangeToken instances to clean up their resources
        DisposeEnhancedTokens();

        _logger.LogDebug("BlobFileProvider disposed");
        
        GC.SuppressFinalize(this);
    }

    private void DisposeEnhancedTokens()
    {
        // Collect all live tokens from the cache using a single TryGetTarget per entry
        var tokensToDispose = new List<EnhancedBlobChangeToken>();
        foreach (var kvp in _tokenCache)
        {
            if (kvp.Value.TryGetTarget(out var token))
            {
                tokensToDispose.Add(token);
            }
        }
        
        // Clear the cache immediately to prevent new references
        _tokenCache.Clear();
        
        // Dispose all collected tokens
        foreach (var token in tokensToDispose)
        {
            try
            {
                token.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing enhanced change token during BlobFileProvider disposal");
            }
        }
        
        if (tokensToDispose.Count > 0)
        {
            _logger.LogDebug("Disposed {Count} cached enhanced change tokens", tokensToDispose.Count);
        }
    }
}