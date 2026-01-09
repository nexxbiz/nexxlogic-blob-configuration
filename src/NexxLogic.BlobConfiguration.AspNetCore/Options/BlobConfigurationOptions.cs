using System.ComponentModel.DataAnnotations;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

public class BlobConfigurationOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Blob Container URL. The URL must contain a SAS token with at least Read and List permissions.
    /// </summary>
    /// <value>
    /// The BLOB container URL in the following format: https://{storageAccountName}.blob.core.windows.net/{containerName}?{sasToken}
    /// </value>
    public string BlobContainerUrl { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string? Prefix { get; set; }

    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }

    /// <summary>
    /// Reload interval in milliseconds
    /// </summary>
    [Range(1, 86400000, ErrorMessage = "ReloadInterval must be between 1 millisecond and 86400000 milliseconds (24 hours).")]
    public int ReloadInterval { get; set; } = 30_000;
    
    /// <summary>
    /// Debounce delay in seconds to prevent rapid consecutive reloads
    /// </summary>
    [Range(0, 3600, ErrorMessage = "DebounceDelaySeconds must be between 0 and 3600 seconds (1 hour). Use 0 to disable debouncing.")]
    public int DebounceDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Maximum file size in MB for content-based hash calculation
    /// </summary>
    [Range(1, 1024, ErrorMessage = "MaxFileContentHashSizeMb must be between 1 and 1024 MB.")]
    public int MaxFileContentHashSizeMb { get; set; } = 1;

    /// <summary>
    /// Interval in seconds for polling blob changes during watching
    /// </summary>
    [Range(1, 86400, ErrorMessage = "WatchingIntervalSeconds must be between 1 second and 86400 seconds (24 hours).")]
    public int WatchingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Delay in seconds before retrying after an error during blob watching.
    /// Allows configuring extended backoff in case of repeated failures; values up to 7200 seconds (2 hours) are supported.
    /// The default is 60 seconds, which is suitable for most scenarios requiring responsive error recovery.
    /// </summary>
    [Range(1, 7200, ErrorMessage = "ErrorRetryDelaySeconds must be between 1 second and 7200 seconds (2 hours).")]
    public int ErrorRetryDelaySeconds { get; set; } = 60;
}