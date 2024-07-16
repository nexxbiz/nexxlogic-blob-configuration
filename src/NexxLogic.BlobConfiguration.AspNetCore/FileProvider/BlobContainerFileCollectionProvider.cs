using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobContainerFileCollectionProvider : IFileProvider
{
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobContainerFileCollectionProvider> _logger;

    private BlobChangeToken _changeToken = new();
    private BlobContainerFileCollection? blobFileCollection;

    /// <summary>
    /// Set to true on initial load. Also set to true when any changes/updates are detected to the blob container folder; e.g. updating or adding files.
    /// Always set to false after loading/re-loading is handled.
    /// </summary>
    private volatile bool loadPending;

    public BlobContainerFileCollectionProvider(
        IBlobContainerClientFactory blobContainerClientFactory,
        BlobConfigurationOptions blobConfig,
        ILogger<BlobContainerFileCollectionProvider> logger)
    {
        _blobConfig = blobConfig;
        _blobContainerClientFactory = blobContainerClientFactory;
        _logger = logger;

        loadPending = true;
    }

    public IFileInfo GetFileInfo(string prefix)
    {
        HandleLoading(prefix);
        return blobFileCollection!;
    }

    public IDirectoryContents GetDirectoryContents(string prefix)
    {
        HandleLoading(prefix);
        var blobDirectoryContents = new BlobDirectoryContents(containerExists: true, new List<IFileInfo> { blobFileCollection! });
        return blobDirectoryContents;
    }

    public IChangeToken Watch(string filter)
    {
        var client = _blobContainerClientFactory.GetBlobContainerClient();
        _ = WatchBlobContainerUpdates(client, _changeToken.CancellationToken);
        return _changeToken;
    }

    void HandleLoading(string prefix)
    {
        if (prefix != _blobConfig.Prefix && !string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException($"Could not return file info for sub path '{prefix}'. You can specify a folder name for the blob container. " +
                $"In this case, please make sure you are assigning the value to the Prefix property of the Blob Configuration.");
        }

        if (blobFileCollection is null)
        {
            blobFileCollection = new BlobContainerFileCollection(_blobContainerClientFactory, prefix);
            blobFileCollection.Load();
        }
        else if (loadPending)
        {
            blobFileCollection.Load();
        }

        loadPending = false;
    }    

    private async Task WatchBlobContainerUpdates(BlobContainerClient blobContainerClient, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_blobConfig.ReloadInterval, token);
                _logger.LogInformation("Checking whether to re-load the blob container folder");

                if (loadPending)
                {
                    _logger.LogWarning("The blob container folder is currently being re-loaded");
                    continue;
                }

                if (blobFileCollection is null)
                {
                    _logger.LogWarning("The blob container folder is not loaded yet");
                    continue;
                }

                await foreach (var blobItemPage in blobContainerClient.GetBlobsAsync(prefix: _blobConfig.Prefix, cancellationToken: token).AsPages())
                {
                    foreach (var blobItem in blobItemPage.Values)
                    {
                        if (!blobFileCollection!.FolderContainsBlobFile(blobItem.Name))
                        {
                            _logger.LogWarning("New blob file was added to container / folder");
                            RaiseChanged();
                            break;
                        }

                        var blobClient = blobContainerClient.GetBlobClient(blobItem.Name);
                        var blobProperties = await blobClient.GetPropertiesAsync(cancellationToken: token);
                        if (blobProperties.Value.LastModified.Ticks > blobFileCollection.LastModified.Ticks)
                        {
                            _logger.LogWarning("Blob file was modified");
                            RaiseChanged();
                            break;
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("task cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("error occured {err}", ex.Message);
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
        _logger.LogWarning("Blob Change Raised");
        var previousToken = Interlocked.Exchange(ref _changeToken, new BlobChangeToken());
        loadPending = true;
        previousToken.OnReload();
    }
}