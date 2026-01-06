using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Factory that intelligently selects the most appropriate change detection strategy 
/// based on configuration and runtime context
/// </summary>
public class ChangeDetectionStrategyFactory(BlobConfigurationOptions options) : IChangeDetectionStrategyFactory
{
    private readonly BlobConfigurationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public IChangeDetectionStrategy CreateStrategy(ILogger logger, int maxContentHashSizeMb)
    {
        // Prefer content-based detection for accuracy when file sizes are reasonable
        if (ShouldUseContentBasedDetection(maxContentHashSizeMb))
        {
            return new ContentBasedChangeDetectionStrategy(logger, maxContentHashSizeMb);
        }
        
        // Fall back to ETag detection for performance or large files
        return new ETagChangeDetectionStrategy(logger);
    }

    private static bool ShouldUseContentBasedDetection(int maxContentHashSizeMb)
    {
        // Smart decision logic - this is where the factory adds value
        // Could be extended with additional criteria.

        return maxContentHashSizeMb > 0;
    }
}