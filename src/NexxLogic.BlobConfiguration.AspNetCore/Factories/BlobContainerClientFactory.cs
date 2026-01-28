using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Utilities;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

public class BlobContainerClientFactory(
    BlobConfigurationOptions blobConfig, 
    IBlobServiceClientFactory? blobServiceClientFactory = null) : IBlobContainerClientFactory
{
    public BlobContainerClient GetBlobContainerClient()
    {
        if (!string.IsNullOrWhiteSpace(blobConfig.BlobContainerUrl))
        {
            // Validate that BlobContainerUrl is a valid HTTP/HTTPS URI
            if (!Uri.TryCreate(blobConfig.BlobContainerUrl, UriKind.Absolute, out var containerUri) ||
                (containerUri.Scheme != Uri.UriSchemeHttp && containerUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    $"BlobContainerUrl must be a valid HTTP or HTTPS URI. Provided value: '{blobConfig.BlobContainerUrl}'",
                    nameof(blobConfig.BlobContainerUrl));
            }
            
            // Check if URL contains actual SAS token (not just any query parameters)
            if (SasTokenDetector.HasSasToken(containerUri))
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

        if (string.IsNullOrWhiteSpace(blobConfig.ContainerName))
        {
            throw new InvalidOperationException("ContainerName must be provided when using ConnectionString");
        }
        
        var serviceClient = new BlobServiceClient(blobConfig.ConnectionString);
        return serviceClient.GetBlobContainerClient(blobConfig.ContainerName);
    }
}