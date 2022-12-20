using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace BlobConfigurationProvider;
public class BlobFileProvider : IFileProvider
{
    private readonly IBlobClientFactory _blobClientFactory;
    private readonly BlobConfigurationOptions _blobConfig;

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

    public BlobFileProvider(IBlobClientFactory blobClientFactory, BlobConfigurationOptions blobConfig)
    {
        _blobClientFactory = blobClientFactory;
        _blobConfig = blobConfig;
    }

    /// <summary>
    /// <inheritdoc/>
    /// <para><see cref="ConfigurationProvider"/> does not call this method.</para>
    /// </summary>
    /// <param name="subpath"><inheritdoc/></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public IDirectoryContents GetDirectoryContents(string subpath)
        => throw new NotImplementedException();

    public IFileInfo GetFileInfo(string subpath)
    {
        var blobClient = _blobClientFactory.GetBlobClient(subpath);
        var result = new BlobFileInfo(blobClient);

        _lastModified = result.LastModified.Ticks;
        _loadPending = false;
        _exists = result.Exists;

        return result;
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
                if (_loadPending)
                    continue;

                if (!_exists)
                    break;

                var properties = await blobClient.GetPropertiesAsync(cancellationToken: token);
                if (properties.Value.LastModified.Ticks > _lastModified)
                    RaiseChanged();
            }
            catch (TaskCanceledException)
            {
                break;
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