using System.Collections.Concurrent;
using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.Strategies;

/// <summary>
/// Interface for implementing different blob change detection strategies
/// </summary>
public interface IChangeDetectionStrategy
{
    /// <summary>
    /// Check if the blob has changed since the last check
    /// </summary>
    /// <param name="blobClient">The blob client to check</param>
    /// <param name="blobPath">The blob path for tracking purposes</param>
    /// <param name="contentHashes">Cache for storing previous state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blob has changed, false otherwise</returns>
    Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> contentHashes, CancellationToken cancellationToken);
}