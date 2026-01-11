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
    /// Gets or sets the factory function that creates an instance of <see cref="BlobServiceClient"/>.
    /// The factory provides a customizable way to initialize a <see cref="BlobServiceClient"/> instance
    /// if the connection string is not directly specified.
    /// </summary>
    /// <value>
    /// A delegate function that returns an instance of <see cref="BlobServiceClient"/>.
    /// This property is used when a custom initialization or dependency injection is required to create
    /// the <see cref="BlobServiceClient"/>. If not set, a connection string must be provided.
    /// </value>
    public Func<BlobServiceClient>? BlobServiceClientFactory { get; set; }

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
    public int ReloadInterval { get; set; } = 30_000;
}