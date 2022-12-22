using Azure.Storage.Blobs;
using BlobConfigurationProvider.Options;

namespace BlobConfigurationProvider.Factories;

public class BlobClientFactory : IBlobClientFactory
{
    private readonly BlobConfigurationOptions _blobConfig;

    public BlobClientFactory(BlobConfigurationOptions blobConfig)
    {
        _blobConfig = blobConfig;
    }

    public BlobClient GetBlobClient(string path)
    {
        var serviceClient = new BlobServiceClient(_blobConfig.ConnectionString);
        var containerClient = serviceClient.GetBlobContainerClient(_blobConfig.ContainerName);
        return containerClient.GetBlobClient(path);
    }
}
