using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public interface IBlobContainerClientFactory
{
    BlobContainerClient GetBlobContainerClient();
}
