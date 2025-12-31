namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

/// <summary>
/// Defines the strategy for detecting changes in blob files
/// </summary>
public enum ChangeDetectionStrategy
{
    /// <summary>
    /// Use ETag-based change detection (lightweight, metadata-based)
    /// </summary>
    ETag = 0,
    
    /// <summary>
    /// Use content-based change detection with SHA256 hashing (accurate, content-based)
    /// </summary>
    ContentBased = 1
}
