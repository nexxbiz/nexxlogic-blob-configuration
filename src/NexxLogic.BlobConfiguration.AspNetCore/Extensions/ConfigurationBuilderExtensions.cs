using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using Azure.Core;
using Azure.Identity;

namespace NexxLogic.BlobConfiguration.AspNetCore.Extensions;

public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Adds JSON blob configuration with a specific TokenCredential.
    /// </summary>
    public static IConfigurationBuilder AddJsonBlob(this IConfigurationBuilder builder, 
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger,
        TokenCredential credential)
    {
        var options = new BlobConfigurationOptions();
        configure(options);
        RequiredBlobNameBlobConfigurationOptionsValidator.ValidateAndThrow(options);
        
        var blobServiceClientLogger = new NullLogger<BlobServiceClientFactory>();
        var blobServiceClientFactory = new BlobServiceClientFactory(blobServiceClientLogger, credential);
        var blobContainerClientFactory = new BlobContainerClientFactory(options, blobServiceClientFactory);
        var blobClientFactory = new BlobClientFactory(blobContainerClientFactory);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(
                blobClientFactory,
                blobContainerClientFactory,
                blobServiceClientFactory,
                options, 
                logger
            );
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;
            source.Path = options.BlobName;
        });
    }

    /// <summary>
    /// Adds JSON blob configuration with credentials configured from environment or configuration.
    /// Supports environment-based credential selection:
    /// - AZURE_CLIENT_ID + AZURE_CLIENT_SECRET + AZURE_TENANT_ID = ClientSecretCredential
    /// - AZURE_CLIENT_ID only = ManagedIdentityCredential with client ID
    /// - Neither = DefaultAzureCredential (fallback to multiple sources)
    /// </summary>
    public static IConfigurationBuilder AddJsonBlobWithEnvironmentCredentials(this IConfigurationBuilder builder,
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger)
    {
        var credential = CreateCredentialFromEnvironment();
        return builder.AddJsonBlob(configure, logger, credential);
    }

    /// <summary>
    /// Adds JSON blob configuration with credentials from IConfiguration.
    /// Looks for Azure credential settings in configuration under "Azure" section.
    /// </summary>
    public static IConfigurationBuilder AddJsonBlobWithConfiguredCredentials(this IConfigurationBuilder builder,
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger,
        IConfiguration configuration)
    {
        var credential = CreateCredentialFromConfiguration(configuration);
        return builder.AddJsonBlob(configure, logger, credential);
    }

    private static TokenCredential CreateCredentialFromEnvironment()
    {
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

        // Service Principal authentication
        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId))
        {
            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }

        // Managed Identity with specific client ID
        if (!string.IsNullOrEmpty(clientId) && string.IsNullOrEmpty(clientSecret))
        {
            return new ManagedIdentityCredential(clientId);
        }

        // Default fallback - tries multiple credential sources
        return new DefaultAzureCredential();
    }

    private static TokenCredential CreateCredentialFromConfiguration(IConfiguration configuration)
    {
        var azureSection = configuration.GetSection("Azure");
        var clientId = azureSection["ClientId"];
        var clientSecret = azureSection["ClientSecret"];
        var tenantId = azureSection["TenantId"];

        // Service Principal from configuration
        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId))
        {
            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }

        // Managed Identity with client ID from configuration
        if (!string.IsNullOrEmpty(clientId))
        {
            return new ManagedIdentityCredential(clientId);
        }

        // Check for chained credential configuration
        var credentialChain = azureSection.GetSection("CredentialChain").GetChildren()
            .Select(section => section.Value)
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();
        if (credentialChain.Length > 0)
        {
            var credentials = new List<TokenCredential>();
            foreach (var credType in credentialChain)
            {
                credentials.Add((credType?.ToLowerInvariant()) switch
                {
                    "managedidentity" => new ManagedIdentityCredential(),
                    "azurecli" => new AzureCliCredential(),
                    "visualstudio" => new VisualStudioCredential(),
                    "visualstudiocode" => new VisualStudioCodeCredential(),
                    _ => throw new InvalidOperationException($"Unsupported credential type: {credType ?? "null"}")
                });
            }
            return new ChainedTokenCredential(credentials.ToArray());
        }

        // Default fallback
        return new DefaultAzureCredential();
    }
}