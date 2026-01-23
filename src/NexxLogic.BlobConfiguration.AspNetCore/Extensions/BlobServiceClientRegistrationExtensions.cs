using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;

namespace NexxLogic.BlobConfiguration.AspNetCore.Extensions;

/// <summary>
/// Extension methods for registering BlobServiceClientFactory with different authentication strategies.
/// These are examples showing how to configure TokenCredential for different deployment scenarios.
/// </summary>
public static class BlobServiceClientRegistrationExtensions
{
    /// <summary>
    /// Registers BlobServiceClientFactory with DefaultAzureCredential (recommended for most scenarios).
    /// This provides automatic fallback through multiple credential sources.
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithDefaultCredential(this IServiceCollection services)
    {
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }

    /// <summary>
    /// Registers BlobServiceClientFactory with Managed Identity (recommended for Azure-hosted applications).
    /// Use this when you want to explicitly use Managed Identity without fallbacks.
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithManagedIdentity(
        this IServiceCollection services, 
        string? clientId = null)
    {
        services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential(clientId));
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }

    /// <summary>
    /// Registers BlobServiceClientFactory with Service Principal credentials.
    /// Use this for service-to-service authentication scenarios.
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithServicePrincipal(
        this IServiceCollection services,
        string tenantId,
        string clientId,
        string clientSecret)
    {
        services.AddSingleton<TokenCredential>(_ => new ClientSecretCredential(tenantId, clientId, clientSecret));
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }

    /// <summary>
    /// Registers BlobServiceClientFactory with chained credentials (multiple fallbacks).
    /// Tries multiple credential sources in order until one succeeds.
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithChainedCredential(this IServiceCollection services)
    {
        services.AddSingleton<TokenCredential>(_ => new ChainedTokenCredential(
            new ManagedIdentityCredential(),      // Try Managed Identity first
            new AzureCliCredential(),             // Fallback to Azure CLI
            new VisualStudioCredential(),         // Fallback to Visual Studio
            new VisualStudioCodeCredential()      // Fallback to VS Code
        ));
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }

    /// <summary>
    /// Registers BlobServiceClientFactory with custom TokenCredential.
    /// Use this when you have specific authentication requirements.
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithCustomCredential(
        this IServiceCollection services,
        TokenCredential credential)
    {
        services.AddSingleton(credential);
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }

    /// <summary>
    /// Registers BlobServiceClientFactory with a credential factory function.
    /// Use this for complex credential configuration logic.
    /// 
    /// Example for environment-specific credentials:
    /// services.AddBlobServiceClientFactoryWithCredentialFactory(serviceProvider =>
    /// {
    ///     var env = serviceProvider.GetRequiredService&lt;IHostEnvironment&gt;();
    ///     return env.IsDevelopment() 
    ///         ? new AzureCliCredential()
    ///         : new ManagedIdentityCredential();
    /// });
    /// </summary>
    public static IServiceCollection AddBlobServiceClientFactoryWithCredentialFactory(
        this IServiceCollection services,
        Func<IServiceProvider, TokenCredential> credentialFactory)
    {
        services.AddSingleton(credentialFactory);
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        return services;
    }
}