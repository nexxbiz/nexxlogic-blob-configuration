using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

/// <summary>
/// Factory for creating BlobServiceClient instances with proper authentication configuration.
/// </summary>
public interface IBlobServiceClientFactory
{
    /// <summary>
    /// Creates a BlobServiceClient based on the provided configuration.
    /// </summary>
    /// <param name="config">The blob configuration options containing connection details.</param>
    /// <returns>A BlobServiceClient instance, or null if enhanced features are not available (e.g., SAS token usage).</returns>
    BlobServiceClient? CreateBlobServiceClient(BlobConfigurationOptions config);
}
