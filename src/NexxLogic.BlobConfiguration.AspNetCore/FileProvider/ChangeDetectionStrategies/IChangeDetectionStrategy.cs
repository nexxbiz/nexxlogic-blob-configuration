namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Interface for implementing different blob change detection strategies
/// </summary>
public interface IChangeDetectionStrategy
{
    /// <summary>
    /// Checks if a blob has changed since the last check
    /// </summary>
    /// <param name="context">Context containing all necessary information for change detection</param>
    /// <returns>True if the blob has changed, false otherwise</returns>
    Task<bool> HasChangedAsync(ChangeDetectionContext context);
}