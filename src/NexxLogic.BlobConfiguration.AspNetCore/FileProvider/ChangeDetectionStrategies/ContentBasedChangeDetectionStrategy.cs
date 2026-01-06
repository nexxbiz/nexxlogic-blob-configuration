using System.Collections.Concurrent;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Content-based change detection strategy using SHA256 hashing (accurate, content-based)
/// </summary>
public class ContentBasedChangeDetectionStrategy(ILogger logger, int maxContentHashSizeMb) : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> contentHashes, CancellationToken cancellationToken)
    {
        try
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

            // Keep ETag-based state in sync even when using content hashing, so that
            // switching between strategies does not lose change-detection history.
            var etagKey = $"{blobPath}:etag";
            var currentEtag = properties.Value.ETag.ToString();
            var previousEtag = contentHashes.GetValueOrDefault(etagKey);
            if (currentEtag != previousEtag)
            {
                contentHashes[etagKey] = currentEtag;
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
                        ? previousHash[..8] + "..."
                        : previousHash);

                var newHashDisplay = currentHash.Length >= 8
                    ? currentHash[..8] + "..."
                    : currentHash;

                logger.LogInformation("Content change detected for blob {BlobPath}. Hash changed from {OldHash} to {NewHash}",
                    blobPath, oldHashDisplay, newHashDisplay);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check content changes for blob {BlobPath}", blobPath);
            return false;
        }
    }
}