using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

/// <summary>
/// Factory for creating BlobServiceClient instances with proper authentication configuration.
/// 
/// Authentication Requirements:
/// 
/// 1. ConnectionString: Provides full authentication - enables enhanced features
///    Example: "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=..."
/// 
/// 2. BlobContainerUrl with SAS token: Falls back to legacy mode (no enhanced features)
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer?sv=2020-08-04&amp;ss=b..."
///    Rationale: SAS tokens provide container-level access, but enhanced features need storage account-level access
/// 
/// 3. BlobContainerUrl without SAS token: Uses DefaultAzureCredential, enables enhanced features
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer"
///    Requires one of:
///    - Managed Identity (recommended for Azure-hosted applications)
///    - Azure CLI authentication: `az login` (for local development)
///    - Environment variables: AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID
///    - Visual Studio or VS Code authentication
///    - WorkloadIdentity (for AKS with federated identity)
/// 
/// If authentication fails, the factory returns null to indicate fallback to legacy mode.
/// </summary>
public class BlobServiceClientFactory(ILogger<BlobServiceClientFactory> logger) : IBlobServiceClientFactory
{
    public BlobServiceClient? CreateBlobServiceClient(BlobConfigurationOptions config)
    {
        try
        {
            if (!string.IsNullOrEmpty(config.ConnectionString))
            {
                logger.LogDebug("Creating BlobServiceClient using connection string");
                return new BlobServiceClient(config.ConnectionString);
            }

            if (!string.IsNullOrEmpty(config.BlobContainerUrl))
            {
                var containerUri = new Uri(config.BlobContainerUrl);
                return CreateBlobServiceClientFromUri(containerUri);
            }

            logger.LogWarning("Neither ConnectionString nor BlobContainerUrl is configured. Cannot create BlobServiceClient.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, 
                "Failed to create BlobServiceClient for enhanced features. " +
                "If using BlobContainerUrl without SAS token, ensure Azure credentials are properly configured " +
                "(Managed Identity, Azure CLI, environment variables, or IDE authentication). " +
                "Falling back to legacy mode.");
            return null;
        }
    }

    private BlobServiceClient? CreateBlobServiceClientFromUri(Uri containerUri)
    {
        // Create BlobServiceClient using DefaultAzureCredential for authentication
        // Note: SAS token detection is now handled by BlobFileProvider for better test compatibility
        return CreateBlobServiceClientWithDefaultAzureCredentials(containerUri);
    }

    private BlobServiceClient CreateBlobServiceClientWithDefaultAzureCredentials(Uri containerUri)
    {
        // This requires one of the following to be configured in the environment:
        // - Managed Identity (recommended for Azure-hosted applications)
        // - Azure CLI authentication (for local development)
        // - Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
        // - Visual Studio or VS Code authentication
        var serviceUri = new Uri($"{containerUri.Scheme}://{containerUri.Host}");
        var credential = new DefaultAzureCredential();

        logger.LogInformation(
            "Creating BlobServiceClient using DefaultAzureCredential. " +
            "Ensure Azure credentials are configured " +
            "(Managed Identity, Azure CLI, environment variables, or IDE authentication).");

        return new BlobServiceClient(serviceUri, credential);
    }
}
