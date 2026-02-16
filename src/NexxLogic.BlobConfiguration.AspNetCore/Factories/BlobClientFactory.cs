using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public sealed class BlobClientFactory(IBlobContainerClientFactory blobContainerClientFactory) : IBlobClientFactory
{
    public BlobClient GetBlobClient(string path)
    {
        return blobContainerClientFactory
            .GetBlobContainerClient()
            .GetBlobClient(path);
    }
}