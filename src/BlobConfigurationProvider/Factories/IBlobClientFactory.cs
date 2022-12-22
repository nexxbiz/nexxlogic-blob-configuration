using Azure.Storage.Blobs;

namespace BlobConfigurationProvider.Factories;

public interface IBlobClientFactory
{
    BlobClient GetBlobClient(string path);
}
