using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// ETag-based change detection strategy (lightweight, metadata-based)
/// </summary>
internal class ETagChangeDetectionStrategy(ILogger logger) : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(BlobClient blobClient, string blobPath, ConcurrentDictionary<string, string> contentHashes, CancellationToken cancellationToken)
    {
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        var currentETag = properties.Value.ETag.ToString();

        var previousETag = contentHashes.GetValueOrDefault($"{blobPath}:etag");
        if (currentETag != previousETag)
        {
            contentHashes[$"{blobPath}:etag"] = currentETag;
            logger.LogInformation("ETag change detected for blob {BlobPath}. ETag changed from {OldETag} to {NewETag}",
                blobPath, previousETag, currentETag);
            return true;
        }

        return false;
    }
}
