using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Utilities;

namespace NexxLogic.BlobConfiguration.AspNetCore.Factories;

/// <summary>
/// Factory for creating BlobServiceClient instances with flexible authentication configuration.
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
/// Authentication Strategies:
/// 
/// The factory accepts a TokenCredential via dependency injection, allowing flexible authentication:
/// 
/// 1. DefaultAzureCredential (recommended for most scenarios):
///    services.AddSingleton&lt;TokenCredential&gt;(_ => new DefaultAzureCredential());
/// 
/// 2. Managed Identity (for Azure-hosted applications):
///    services.AddSingleton&lt;TokenCredential&gt;(_ => new ManagedIdentityCredential());
/// 
/// 3. Service Principal (for service-to-service authentication):
///    services.AddSingleton&lt;TokenCredential&gt;(_ => new ClientSecretCredential(tenantId, clientId, clientSecret));
/// 
/// 4. Local Development (Visual Studio/VS Code):
///    services.AddSingleton&lt;TokenCredential&gt;(_ => new VisualStudioCredential());
/// 
/// 5. Chained Credentials (multiple fallbacks):
///    services.AddSingleton&lt;TokenCredential&gt;(_ => new ChainedTokenCredential(
///        new ManagedIdentityCredential(),
///        new AzureCliCredential(),
///        new VisualStudioCredential()));
/// 
/// 6. Environment-specific (different per environment):
///    services.AddSingleton&lt;TokenCredential&gt;(serviceProvider =>
///    {
///        var env = serviceProvider.GetRequiredService&lt;IHostEnvironment&gt;();
///        return env.IsDevelopment() 
///            ? new AzureCliCredential()
///            : new ManagedIdentityCredential();
///    });
/// 
/// Configuration Requirements:
/// 
/// 1. ConnectionString: Provides full authentication - enables enhanced features
///    Example: "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=..."
///    Note: When ConnectionString is provided, TokenCredential is not used
/// 
/// 2. BlobContainerUrl with SAS token: Falls back to legacy mode (no enhanced features)
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer?sv=2020-08-04&amp;ss=b..."
///    Rationale: SAS tokens provide container-level access, but enhanced features need storage account-level access
/// 
/// 3. BlobContainerUrl without SAS token: Uses provided TokenCredential, enables enhanced features
///    Example: "https://mystorageaccount.blob.core.windows.net/mycontainer"
///    Requires TokenCredential to be properly configured in DI
/// 
/// Runtime failures (network issues, permission problems) will throw exceptions for the caller to handle.
/// </summary>
public class BlobServiceClientFactory(
    ILogger<BlobServiceClientFactory> logger, 
    TokenCredential? tokenCredential = null) : IBlobServiceClientFactory
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

        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            // Validate connection string format and required components
            var connectionStringValidation = ValidateConnectionString(config.ConnectionString);
            if (!connectionStringValidation.IsValid)
            {
                return connectionStringValidation;
            }
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

            // Validate that the URL has proper components for Azure Blob Storage
            if (string.IsNullOrEmpty(uri.Host) ||
                !uri.Host.Contains('.') ||
                uri.Host.Split('.').Count(x => !string.IsNullOrWhiteSpace(x)) < 2)
            {
                return (false, "BlobContainerUrl must have a valid hostname (e.g., 'storageaccount.blob.core.windows.net')");
            }

            // Check if URL contains SAS token (fallback scenario)
            if (SasTokenDetector.HasSasToken(uri))
            {
                return (false, "BlobContainerUrl contains SAS token - SAS tokens provide container-level access, but enhanced features need storage account-level access");
            }

            // For BlobContainerUrl without SAS token, we need a TokenCredential
            if (tokenCredential == null)
            {
                return (false, "BlobContainerUrl provided without SAS token, but no TokenCredential is configured. Register a TokenCredential in dependency injection.");
            }
        }

        return (true, null);
    }

    private static (bool IsValid, string? Reason) ValidateConnectionString(string connectionString)
    {
        try
        {
            var keyValuePairs = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(kvp => kvp.Length == 2)
                .ToDictionary(kvp => kvp[0].Trim(), kvp => kvp[1].Trim(), StringComparer.OrdinalIgnoreCase);

            // Check for required AccountName
            if (!keyValuePairs.TryGetValue("AccountName", out var accountName) || string.IsNullOrWhiteSpace(accountName))
            {
                return (false, "ConnectionString must contain a valid AccountName");
            }

            // Check for required AccountKey (if not using other auth methods)
            if (keyValuePairs.TryGetValue("AccountKey", out var accountKey))
            {
                if (string.IsNullOrWhiteSpace(accountKey))
                {
                    return (false, "ConnectionString AccountKey cannot be empty");
                }
            }
            else if (!keyValuePairs.ContainsKey("SharedAccessSignature"))
            {
                // If no AccountKey and no SAS, this is an incomplete connection string for authentication purposes
                return (false, "ConnectionString must contain either AccountKey or SharedAccessSignature");
            }

            return (true, null);
        }
        catch (Exception)
        {
            return (false, "ConnectionString format is invalid");
        }
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
        // Use the injected TokenCredential for authentication
        // This provides flexibility for different authentication strategies
        return CreateBlobServiceClientWithCredential(containerUri, tokenCredential!);
    }

    private BlobServiceClient CreateBlobServiceClientWithCredential(Uri containerUri, TokenCredential credential)
    {
        // Extract storage account service URI from the container URI
        var serviceUri = new Uri($"{containerUri.Scheme}://{containerUri.Host}");
        
        logger.LogInformation(
            "Creating BlobServiceClient using provided TokenCredential ({CredentialType}) for service URI: {ServiceUri}", 
            credential.GetType().Name, 
            serviceUri);

        return new BlobServiceClient(serviceUri, credential);
    }
}