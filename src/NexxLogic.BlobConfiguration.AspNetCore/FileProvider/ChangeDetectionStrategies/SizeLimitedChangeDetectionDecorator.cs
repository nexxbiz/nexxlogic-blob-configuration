using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Decorator strategy that delegates to primary or fallback strategy based on blob size
/// </summary>
internal class SizeLimitedChangeDetectionDecorator(
    IChangeDetectionStrategy primaryStrategy,
    IChangeDetectionStrategy fallbackStrategy,
    int maxSizeMb,
    ILogger logger)
    : IChangeDetectionStrategy
{
    public async Task<bool> HasChangedAsync(ChangeDetectionContext context)
    {
        if (context.Properties.ContentLength > maxSizeMb * 1024 * 1024)
        {
            logger.LogDebug("Blob {BlobPath} too large ({Size} bytes) for primary strategy, using fallback strategy", 
                context.BlobPath, context.Properties.ContentLength);
            
            return await fallbackStrategy.HasChangedAsync(context);
        }

        return await primaryStrategy.HasChangedAsync(context);
    }
}