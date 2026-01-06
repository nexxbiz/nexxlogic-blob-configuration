using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.Strategies;

/// <summary>
/// ETag-based change detection strategy (lightweight, metadata-based)
/// </summary>
public class ETagChangeDetectionStrategy(ILogger logger) : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> contentHashes, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var currentETag = properties.Value.ETag.ToString();

            var previousETag = contentHashes.GetValueOrDefault(blobPath);
            if (currentETag != previousETag)
            {
                contentHashes[blobPath] = currentETag;
                logger.LogInformation("ETag change detected for blob {BlobPath}. ETag changed from {OldETag} to {NewETag}",
                    blobPath, previousETag, currentETag);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check ETag changes for blob {BlobPath}", blobPath);
            return false;
        }
    }
}