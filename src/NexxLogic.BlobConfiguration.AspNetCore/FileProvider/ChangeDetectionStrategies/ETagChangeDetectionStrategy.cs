using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// ETag-based change detection strategy (lightweight, metadata-based)
/// </summary>
public class ETagChangeDetectionStrategy(ILogger logger) : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> blobFingerprints, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var currentETag = properties.Value.ETag.ToString();

            var etagKey = $"{blobPath}:etag";
            var previousETag = blobFingerprints.GetValueOrDefault(etagKey);
            if (currentETag != previousETag)
            {
                blobFingerprints[etagKey] = currentETag;
                logger.LogInformation("ETag change detected for blob {BlobPath}. ETag changed from {OldETag} to {NewETag}",
                    blobPath, previousETag ?? "null", currentETag);
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