using System.ComponentModel.DataAnnotations;
using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

public class BlobConfigurationOptions
{
    /// <summary>
    /// Gets or sets the connection string used to authenticate and connect to the Azure Blob Storage account.
    /// This property is utilized for creating an instance of <see cref="BlobServiceClient"/> when a factory function
    /// is not provided via <see cref="BlobServiceClientFactory"/>.
    /// </summary>
    /// <value>
    /// A string representing the connection string for the Azure Blob Storage account. If both
    /// <see cref="ConnectionString"/> and <see cref="BlobServiceClientFactory"/> are not provided,
    /// an exception will be thrown during the initialization of the <see cref="BlobServiceClient"/>.
    /// </value>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the Blob Container URL. The URL must contain a SAS token with at least Read and List permissions.
    /// </summary>
    /// <value>
    /// The BLOB container URL in the following format: https://{storageAccountName}.blob.core.windows.net/{containerName}?{sasToken}
    /// </value>
    public string? BlobContainerUrl { get; set; }

    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string? Prefix { get; set; }

    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }

    /// <summary>
    /// Reload interval in milliseconds
    /// </summary>
    [Range(1000, 86400000, ErrorMessage = "ReloadInterval must be between 1000 millisecond (1 second) and 86400000 milliseconds (24 hours).")]
    public int ReloadInterval { get; set; } = 30_000;
    
    /// <summary>
    /// Debounce delay to prevent rapid consecutive reloads
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00", ErrorMessage = "DebounceDelay must be between 0 seconds and 1 hour.")]
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum file size in MB for content-based hash calculation
    /// </summary>
    [Range(1, 1024, ErrorMessage = "MaxFileContentHashSizeMb must be between 1 and 1024 MB.")]
    public int MaxFileContentHashSizeMb { get; set; } = 1;

    /// <summary>
    /// Interval for polling blob changes during watching
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00", ErrorMessage = "WatchingInterval must be between 1 second and 24 hours.")]
    public TimeSpan WatchingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay before retrying after an error during blob watching.
    /// Allows configuring extended backoff in case of repeated failures; values up to 2 hours are supported.
    /// The default is 1 minute, which is suitable for most scenarios requiring responsive error recovery.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "02:00:00", ErrorMessage = "ErrorRetryDelay must be between 1 second and 2 hours.")]
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}