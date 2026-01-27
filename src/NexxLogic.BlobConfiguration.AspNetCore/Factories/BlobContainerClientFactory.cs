using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory(
    BlobConfigurationOptions blobConfig, 
    IBlobServiceClientFactory? blobServiceClientFactory = null) : IBlobContainerClientFactory
{
    public BlobContainerClient GetBlobContainerClient()
    {
        if (!string.IsNullOrWhiteSpace(blobConfig.BlobContainerUrl))
        {
            var containerUri = new Uri(blobConfig.BlobContainerUrl);
            
            // Check if URL contains SAS token (query string)
            if (!string.IsNullOrEmpty(containerUri.Query))
            {
                // URL has SAS token - use anonymous access
                return new BlobContainerClient(containerUri);
            }
            
            // No SAS token - try to create authenticated client via BlobServiceClient
            var blobServiceClient = blobServiceClientFactory?.CreateBlobServiceClient(blobConfig);
            if (blobServiceClient != null)
            {
                // Extract container name from URL path and use authenticated service client
                var containerName = containerUri.Segments.LastOrDefault()?.TrimEnd('/') ?? 
                                  throw new InvalidOperationException($"Cannot extract container name from URL: {blobConfig.BlobContainerUrl}");
                return blobServiceClient.GetBlobContainerClient(containerName);
            }
            
            // Fallback to anonymous access (will fail for private containers without SAS)
            return new BlobContainerClient(containerUri);
        }

        // Use connection string path
        if (string.IsNullOrWhiteSpace(blobConfig.ConnectionString))
        {
            throw new InvalidOperationException("Either BlobContainerUrl or ConnectionString must be provided");
        }
        
        var serviceClient = new BlobServiceClient(blobConfig.ConnectionString);
        return serviceClient.GetBlobContainerClient(blobConfig.ContainerName);
    }
}