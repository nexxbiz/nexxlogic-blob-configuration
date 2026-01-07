using System.Collections.Concurrent;
using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Interface for implementing different blob change detection strategies
/// </summary>
public interface IChangeDetectionStrategy
{
    /// <summary>
    /// Checks if a blob has changed since the last check
    /// </summary>
    /// <param name="blobClient">The blob client to check</param>
    /// <param name="blobPath">The path of the blob</param>
    /// <param name="blobFingerprints">Shared dictionary for storing blob fingerprints (hashes, ETags, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blob has changed, false otherwise</returns>
    Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> blobFingerprints, CancellationToken cancellationToken);
}