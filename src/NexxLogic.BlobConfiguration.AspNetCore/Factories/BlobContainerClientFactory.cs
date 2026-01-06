using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory(BlobConfigurationOptions blobConfig) : IBlobContainerClientFactory
{
    public BlobContainerClient GetBlobContainerClient()
    {
        if(!string.IsNullOrWhiteSpace(blobConfig.BlobContainerUrl))
        {
            return new BlobContainerClient(new Uri(blobConfig.BlobContainerUrl));
        }

        var serviceClient = new BlobServiceClient(blobConfig.ConnectionString);
        return serviceClient.GetBlobContainerClient(blobConfig.ContainerName);
    }
}