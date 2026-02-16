using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// ETag-based change detection strategy (fast, metadata-based)
/// </summary>
public class ETagChangeDetectionStrategy() : IChangeDetectionStrategy
{
    private readonly ILogger? _logger;
    
    public ETagChangeDetectionStrategy(ILogger logger) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public Task<bool> HasChangedAsync(ChangeDetectionContext context)
    {
        try
        {
            var currentETag = context.Properties.ETag.ToString();

            var etagKey = $"{context.BlobPath}:etag";
            var previousETag = context.BlobFingerprints.GetValueOrDefault(etagKey);
            if (currentETag != previousETag)
            {
                context.BlobFingerprints[etagKey] = currentETag;
                _logger.LogInformation("ETag change detected for blob {BlobPath}. ETag changed from {OldETag} to {NewETag}",
                    context.BlobPath, previousETag ?? "null", currentETag);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check ETag changes for blob {BlobPath}", context.BlobPath);
            return Task.FromResult(false);
        }
    }
}