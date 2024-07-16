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

    /// <summary>
    /// Gets or sets the SAS URL for reading properties and meta data of the Blob Container itself. Make sure you have selected Container as one 
    /// of the allowed resource types, and that you have selected the Read and List permissions.
    /// </summary>
    /// <value>
    /// The Blob Service SAS URL
    /// </value>
    public string BlobContainerServiceSasUrl { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string? Prefix { get; set; }

    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }

    /// <summary>
    /// Reload interval in milliseconds
    /// </summary>
    public int ReloadInterval { get; set; } = 30_000;
}