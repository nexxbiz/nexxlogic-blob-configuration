using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory : IBlobContainerClientFactory
{
    private readonly BlobConfigurationOptions _blobConfig;

    public BlobContainerClientFactory(BlobConfigurationOptions blobConfig)
    {
        _blobConfig = blobConfig;
    }

    public BlobContainerClient GetBlobContainerClient(string path)
    {
        var serviceClient = new BlobServiceClient(_blobConfig.ConnectionString);
        return serviceClient.GetBlobContainerClient(_blobConfig.ContainerName);
    }
}
