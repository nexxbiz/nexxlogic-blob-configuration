using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using System.IO;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobFileProvider : IFileProvider
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobFileProvider> _logger;

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
        var client = _blobClientFactory.GetBlobClient(filter);
        _ = WatchBlobUpdate(client, _changeToken.CancellationToken);
        return _changeToken;
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
                    break;
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
}