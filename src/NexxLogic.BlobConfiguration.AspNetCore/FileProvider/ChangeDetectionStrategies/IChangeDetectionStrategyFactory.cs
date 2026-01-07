using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Factory for creating change detection strategies based on configuration and context
/// </summary>
public interface IChangeDetectionStrategyFactory
{
    /// <summary>
    /// Creates the appropriate change detection strategy based on configuration
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    /// <returns>The most suitable change detection strategy for the current configuration</returns>
    IChangeDetectionStrategy CreateStrategy(ILogger logger);
}