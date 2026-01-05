using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory(BlobConfigurationOptions blobConfig) : IBlobContainerClientFactory
{
    public BlobContainerClient GetBlobContainerClient()
    {
        if(!string.IsNullOrWhiteSpace(blobConfig.BlobContainerUrl))
            return new(new(blobConfig.BlobContainerUrl));

        var serviceClient = GetBlobServiceClient();
        return serviceClient.GetBlobContainerClient(blobConfig.ContainerName);
    }
    
    private BlobServiceClient GetBlobServiceClient()
    {
        if (blobConfig.BlobServiceClientFactory != null)
            return blobConfig.BlobServiceClientFactory();

        if (!string.IsNullOrWhiteSpace(blobConfig.ConnectionString))
            return new(blobConfig.ConnectionString);

        throw new InvalidOperationException("No valid BlobServiceClient could be created. Please provide either a ConnectionString or a BlobServiceClientFactory.");
    }
}
