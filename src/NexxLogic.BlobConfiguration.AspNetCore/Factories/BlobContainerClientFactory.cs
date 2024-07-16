using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory : IBlobContainerClientFactory
{
    private readonly BlobConfigurationOptions _blobConfig;
    private BlobContainerClient? clientSingleton;

    public BlobContainerClientFactory(BlobConfigurationOptions blobConfig)
    {
        _blobConfig = blobConfig;
    }

    public BlobContainerClient GetBlobContainerClient()
    {
        if (clientSingleton is not null)
        {
            return clientSingleton;
        }

        if(!string.IsNullOrWhiteSpace(_blobConfig.BlobContainerUrl))
        {
            clientSingleton = new BlobContainerClient(new Uri(_blobConfig.BlobContainerUrl));
        }
        else
        {
            var serviceClient = new BlobServiceClient(_blobConfig.ConnectionString);
            clientSingleton = serviceClient.GetBlobContainerClient(_blobConfig.ContainerName);
        }

        return clientSingleton;
    }   
}
