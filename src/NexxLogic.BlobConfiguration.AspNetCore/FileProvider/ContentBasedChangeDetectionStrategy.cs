using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// Content-based change detection strategy using SHA256 hashing (accurate, content-based)
/// </summary>
internal class ContentBasedChangeDetectionStrategy(ILogger logger, int maxContentHashSizeMb) : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> contentHashes, CancellationToken cancellationToken)
    {
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        
        // Skip large files for content hashing, fall back to ETag
        if (properties.Value.ContentLength > maxContentHashSizeMb * 1024 * 1024)
        {
            logger.LogDebug("Blob {BlobPath} too large for content hashing, using ETag", blobPath);
            
            // Use ETag fallback logic directly
            var fallbackStrategy = new ETagChangeDetectionStrategy(logger);
            return await fallbackStrategy.HasChangedAsync(blobClient, blobPath, contentHashes, cancellationToken);
        }

        await using var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        var currentHash = Convert.ToBase64String(hashBytes);

        var previousHash = contentHashes.GetValueOrDefault(blobPath);
        if (currentHash != previousHash)
        {
            contentHashes[blobPath] = currentHash;

            var oldHashDisplay = previousHash == null
                ? "..."
                : (previousHash.Length >= 8
                    ? previousHash.Substring(0, 8) + "..."
                    : previousHash);

            logger.LogInformation("Content change detected for blob {BlobPath}. Hash changed from {OldHash} to {NewHash}",
                blobPath, oldHashDisplay, currentHash.Substring(0, 8) + "...");
            return true;
        }

        return false;
    }
}
