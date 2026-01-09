using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public interface IBlobClientFactory
{
    BlobClient GetBlobClient(string path);
}