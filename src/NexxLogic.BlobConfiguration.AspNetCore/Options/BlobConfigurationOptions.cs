using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

public class BlobConfigurationOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ConnectionStringKey { get; set; } = "BlobStorage";

    /// <summary>
    /// Gets or sets the Blob Container URL. The URL must contain a SAS token with at least Read and List permissions.
    /// </summary>
    /// <value>
    /// The BLOB container URL in the following format: https://{storageAccountName}.blob.core.windows.net/{containerName}?{sasToken}
    /// </value>
    public string BlobContainerUrl { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string[] BlobNames { get; set; } = Array.Empty<string>();
    public string? Prefix { get; set; } = "";

    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }

    /// <summary>
    /// Reload interval in milliseconds
    /// </summary>
    public int ReloadInterval { get; set; } = 30_000;
    
    /// <summary>
    /// Debounce delay in seconds to prevent rapid consecutive reloads
    /// </summary>
    public int DebounceDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Change detection strategy to use for monitoring blob modifications
    /// </summary>
    public ChangeDetectionStrategy ChangeDetectionStrategy { get; set; } = ChangeDetectionStrategy.ContentBased;

    /// <summary>
    /// Maximum file size in MB for content-based hash calculation
    /// </summary>
    public int MaxFileContentHashSizeMb { get; set; } = 1;

    /// <summary>
    /// Interval in seconds for polling blob changes during watching
    /// </summary>
    public int WatchingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Delay in seconds before retrying after an error during blob watching
    /// </summary>
    public int ErrorRetryDelaySeconds { get; set; } = 60;
}