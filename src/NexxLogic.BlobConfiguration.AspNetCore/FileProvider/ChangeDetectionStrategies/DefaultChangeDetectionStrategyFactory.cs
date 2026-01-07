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

    public IChangeDetectionStrategy CreateStrategy(ILogger logger)
    {
        // Create primary and fallback strategies
        var contentStrategy = new ContentBasedChangeDetectionStrategy(logger);
        var etagStrategy = new ETagChangeDetectionStrategy(logger);

        // Use decorator pattern to handle size-based strategy selection
        // This cleanly separates the concerns and eliminates tight coupling
        return new SizeLimitedChangeDetectionDecorator(
            primaryStrategy: contentStrategy,
            fallbackStrategy: etagStrategy,
            maxSizeMb: _options.MaxFileContentHashSizeMb,
            logger: logger);
    }
}