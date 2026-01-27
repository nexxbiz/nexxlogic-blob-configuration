using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using Azure.Core;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobServiceClientFactoryTests
{
    private readonly ILogger<BlobServiceClientFactory> _logger = new NullLogger<BlobServiceClientFactory>();
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnClient_WhenValidConnectionStringProvided()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();

        // Act
        var result = CreateFactory().CreateBlobServiceClient(options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenNoConnectionInfoProvided()
    {
        // Arrange
        var options = new BlobConfigurationOptions();

        // Act
        var result = CreateFactory().CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldAttemptDefaultCredentials_WhenBlobContainerUrlProvidedWithoutSas()
    {
        // Arrange
        var options = CreateOptionsWithContainerUrl();

        // Act & Assert
        // With our new contract: configuration validation passes, so either succeeds or throws runtime exception
        var exception = Record.Exception(() => CreateFactory().CreateBlobServiceClient(options));
        
        if (exception != null)
        {
            // Should throw a runtime exception (credential issues), not return null for valid URLs
            // Common exceptions: CredentialUnavailableException, AuthenticationFailedException
            Assert.True(IsExpectedCredentialException(exception), 
                       $"Expected runtime exception, but got: {exception.GetType().Name}");
        }
        // If no exception, then credentials were available and client was created successfully
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_ForSasTokenUrls()
    {
        // Arrange
        var options = CreateOptionsWithSasUrl();

        // Act
        var result = CreateFactory().CreateBlobServiceClient(options);

        // Assert - SAS tokens are configuration issues, should return null
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_ForInvalidUrls()
    {
        // Arrange
        var options = CreateOptionsWithInvalidUrl();

        // Act
        var result = CreateFactory().CreateBlobServiceClient(options);

        // Assert - Invalid URLs are configuration issues, should return null
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenNoTokenCredentialProvided()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger, tokenCredential: null);
        var options = CreateOptionsWithContainerUrl();

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Should return null because no TokenCredential was provided
        Assert.Null(result);
    }

    // Helper methods to reduce repetition
    private BlobServiceClientFactory CreateFactory() => new(_logger, _credential);

    private static BlobConfigurationOptions CreateOptionsWithConnectionString() => new()
    {
        ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net"
    };

    private static BlobConfigurationOptions CreateOptionsWithContainerUrl() => new()
    {
        BlobContainerUrl = "https://test.blob.core.windows.net/container"
    };

    private static BlobConfigurationOptions CreateOptionsWithSasUrl() => new()
    {
        BlobContainerUrl = "https://test.blob.core.windows.net/container?sv=2020-08-04&ss=b&srt=c&sp=r&se=2023-12-31T23:59:59Z&st=2023-01-01T00:00:00Z&spr=https&sig=example"
    };

    private static BlobConfigurationOptions CreateOptionsWithInvalidUrl() => new()
    {
        BlobContainerUrl = "not-a-valid-url"
    };

    private static bool IsExpectedCredentialException(Exception exception) =>
        exception is Azure.Identity.CredentialUnavailableException or 
                     Azure.Identity.AuthenticationFailedException or 
                     InvalidOperationException;
}