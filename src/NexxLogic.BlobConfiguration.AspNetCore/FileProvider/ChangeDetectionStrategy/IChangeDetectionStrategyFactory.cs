using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.Strategies;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// Factory for creating change detection strategies based on configuration and context
/// </summary>
public interface IChangeDetectionStrategyFactory
{
    /// <summary>
    /// Creates the appropriate change detection strategy based on configuration and runtime context
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    /// <param name="maxContentHashSizeMb">Maximum file size for content hashing</param>
    /// <returns>The most suitable change detection strategy for the current context</returns>
    IChangeDetectionStrategy CreateStrategy(ILogger logger, int maxContentHashSizeMb);
}