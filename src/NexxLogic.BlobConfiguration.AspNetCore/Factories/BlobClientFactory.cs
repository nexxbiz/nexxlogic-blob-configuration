using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public sealed class BlobClientFactory : IBlobClientFactory
{
    private readonly IBlobContainerClientFactory blobContainerClientFactory;

    public BlobClientFactory(IBlobContainerClientFactory blobContainerClientFactory)
    {
        this.blobContainerClientFactory = blobContainerClientFactory;
    }

    public BlobClient GetBlobClient(string path)
    {
        return blobContainerClientFactory
            .GetBlobContainerClient()
            .GetBlobClient(path);
    }
}
