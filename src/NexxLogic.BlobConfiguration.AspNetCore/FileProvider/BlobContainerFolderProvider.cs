using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using System.Text.Json.Nodes;
using System.Text;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobContainerFolderProvider : IFileProvider
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly IBlobContainerClientFactory _blobContainerClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;
    private readonly ILogger<BlobContainerFolderProvider> _logger;

    private BlobChangeToken _changeToken = new();
    private BlobContainerFolderInfo? folderInfo;

    /// <summary>
    /// Set to true on initial load. Also set to true when any changes/updates are detected to the blob container folder; e.g. updating or adding files.
    /// Always set to false after loading/re-loading is handled.
    /// </summary>
    private volatile bool loadPending;

    public BlobContainerFolderProvider(IBlobClientFactory blobClientFactory,
        IBlobContainerClientFactory blobContainerClientFactory,
        BlobConfigurationOptions blobConfig,
        ILogger<BlobContainerFolderProvider> logger)
    {
        _blobClientFactory = blobClientFactory;
        _blobConfig = blobConfig;
        _blobContainerClientFactory = blobContainerClientFactory;
        _logger = logger;

        loadPending = true;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        HandleLoading(subpath);
        return folderInfo!;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        HandleLoading(subpath);
        var blobDirectoryContents = new BlobDirectoryContents(containerExists: true, new List<IFileInfo> { folderInfo! });
        return blobDirectoryContents;
    }

    public IChangeToken Watch(string filter)
    {
        var client = _blobContainerClientFactory.GetBlobContainerClient();
        _ = WatchBlobContainerUpdates(client, _changeToken.CancellationToken);
        return _changeToken;
    }

    void HandleLoading(string subpath)
    {
        if (subpath != _blobConfig.Prefix && !string.IsNullOrWhiteSpace(subpath))
        {
            throw new InvalidOperationException($"Could not return file info for sub path '{subpath}'. You can specify a folder name for the blob container. " +
                $"In this case, please make sure you are assigning the value to the Prefix property of the Blob Configuration.");
        }

        if (folderInfo is null)
        {
            folderInfo = new BlobContainerFolderInfo(_blobConfig.ContainerName, subpath);
            LoadFolder(folderInfo);
        }
        else if (loadPending)
        {
            folderInfo.ResetFolder();
            LoadFolder(folderInfo);
        }

        loadPending = false;
    }

    void LoadFolder(BlobContainerFolderInfo folderInfo)
    {
        var containerClient = _blobContainerClientFactory.GetBlobContainerClient();
        var result = new JsonObject();

        long lastModified = 0;
        var blobFileNames = new List<string>();
        foreach (var blobInfoPage in containerClient.GetBlobs(prefix: folderInfo.FolderName).AsPages())
        {
            foreach (var blobInfo in blobInfoPage.Values)
            {
                var blobClient = containerClient.GetBlobClient(blobInfo.Name);
                var properties = blobClient.GetProperties();
                lastModified = Math.Max(properties.Value.LastModified.Ticks, lastModified);
                blobFileNames.Add(blobInfo.Name);

                var blob = new BlobFileInfo(blobClient);

                using var jsonStream = blob.CreateReadStream();
                var jsonFileObject = JsonNode.Parse(jsonStream)?.AsObject()
                    ?? throw new InvalidOperationException($"Blob '{blob.Name}' does not contain a valid JSON object");

                jsonFileObject.ToList().ForEach(p =>
                {
                    if (p.Value is not null)
                    {
                        result.Add(p.Key, JsonNode.Parse(
                            p.Value!.ToJsonString()
                        ));
                    }
                });
            }
        }

        var jsonString = result.ToJsonString();
        var folderBytes = Encoding.UTF8.GetBytes(jsonString);

        folderInfo.PopulateFolderContent(folderBytes, blobFileNames, lastModified);
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

                if (folderInfo is null)
                {
                    _logger.LogWarning("The blob container folder is not loaded yet");
                    continue;
                }

                await foreach (var blobItemPage in blobContainerClient.GetBlobsAsync(prefix: _blobConfig.Prefix, cancellationToken: token).AsPages())
                {
                    foreach (var blobItem in blobItemPage.Values)
                    {
                        if (!folderInfo!.FolderContainsBlobFile(blobItem.Name))
                        {
                            _logger.LogWarning("New blob file was added to container / folder");
                            RaiseChanged();
                            break;
                        }

                        var blobClient = blobContainerClient.GetBlobClient(blobItem.Name);
                        var blobProperties = await blobClient.GetPropertiesAsync(cancellationToken: token);
                        if (blobProperties.Value.LastModified.Ticks > folderInfo.LastModified.Ticks)
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