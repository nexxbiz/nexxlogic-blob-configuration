using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

/// <summary>
/// Factory for creating BlobServiceClient instances with proper authentication configuration.
/// 
/// Error Handling Contract:
/// - Configuration issues (missing credentials, invalid URLs, SAS tokens): Returns null (fallback to legacy mode)
/// - Runtime failures (network issues, permission denied, etc.): Throws exception (caller must handle)
/// 
/// This explicit contract ensures that:
/// 1. Intentional fallback scenarios (e.g., SAS tokens, missing credentials) are handled gracefully
/// 2. Unexpected runtime failures are surfaced to the caller for proper handling
/// 3. Callers who explicitly configured blob storage get clear feedback if something goes wrong
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
/// Runtime failures (network issues, permission problems) will throw exceptions for the caller to handle.
/// </summary>
public class BlobServiceClientFactory(ILogger<BlobServiceClientFactory> logger) : IBlobServiceClientFactory
{
    public BlobServiceClient? CreateBlobServiceClient(BlobConfigurationOptions config)
    {
        // Validate configuration upfront - these are fallback scenarios
        var validationResult = ValidateConfiguration(config);
        if (!validationResult.IsValid)
        {
            logger.LogWarning("Configuration validation failed: {Reason}. Falling back to legacy mode.", validationResult.Reason);
            return null;
        }

        // Configuration is valid - any exceptions from here should propagate as they indicate runtime issues
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

            // This should never happen as ValidateConfiguration should catch this
            throw new InvalidOperationException("Configuration validation passed but no valid configuration found.");
        }
        catch (Exception ex) when (IsConfigurationError(ex))
        {
            // Configuration-related errors - these warrant fallback
            logger.LogWarning(ex, 
                "Configuration error detected: {Message}. " +
                "If using BlobContainerUrl without SAS token, ensure Azure credentials are properly configured " +
                "(Managed Identity, Azure CLI, environment variables, or IDE authentication). " +
                "Falling back to legacy mode.", ex.Message);
            return null;
        }
        // Runtime errors (network, permissions, etc.) will propagate to caller
    }

    private (bool IsValid, string? Reason) ValidateConfiguration(BlobConfigurationOptions config)
    {
        if (string.IsNullOrEmpty(config.ConnectionString) && string.IsNullOrEmpty(config.BlobContainerUrl))
        {
            return (false, "Neither ConnectionString nor BlobContainerUrl is configured");
        }

        if (!string.IsNullOrEmpty(config.BlobContainerUrl))
        {
            if (!Uri.TryCreate(config.BlobContainerUrl, UriKind.Absolute, out var uri))
            {
                return (false, "BlobContainerUrl is not a valid absolute URI");
            }

            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "BlobContainerUrl must use HTTPS");
            }

            // Check if URL contains SAS token (fallback scenario)
            if (uri.Query.Contains("sv=") || uri.Query.Contains("sig="))
            {
                return (false, "BlobContainerUrl contains SAS token - SAS tokens provide container-level access, but enhanced features need storage account-level access");
            }
        }

        return (true, null);
    }

    private static bool IsConfigurationError(Exception ex)
    {
        return ex switch
        {
            // Azure Identity configuration errors
            CredentialUnavailableException => true,
            AuthenticationFailedException when ex.Message.Contains("No credentials") => true,
            AuthenticationFailedException when ex.Message.Contains("DefaultAzureCredential") => true,
            
            // Connection string format errors
            FormatException => true,
            ArgumentException when ex.Message.Contains("connection string") => true,
            ArgumentException when ex.Message.Contains("invalid") => true,
            
            _ => false
        };
    }

    private BlobServiceClient CreateBlobServiceClientFromUri(Uri containerUri)
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
