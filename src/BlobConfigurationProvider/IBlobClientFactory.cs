using Azure.Storage.Blobs;

namespace BlobConfigurationProvider;

public interface IBlobClientFactory
{
    BlobClient GetBlobClient(string path);
}
