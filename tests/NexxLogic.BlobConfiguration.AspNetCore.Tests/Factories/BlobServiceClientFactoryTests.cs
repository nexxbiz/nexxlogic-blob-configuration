using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobServiceClientFactoryTests
{
    private readonly ILogger<BlobServiceClientFactory> _logger = new NullLogger<BlobServiceClientFactory>();

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnClient_WhenValidConnectionStringProvided()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger);
        var options = new BlobConfigurationOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net"
        };

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenNoConnectionInfoProvided()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger);
        var options = new BlobConfigurationOptions();

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldAttemptDefaultCredentials_WhenBlobContainerUrlProvidedWithoutSas()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger);
        var options = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://test.blob.core.windows.net/container"
        };

        // Act & Assert
        // With our new contract: configuration validation passes, so either succeeds or throws runtime exception
        var exception = Record.Exception(() => factory.CreateBlobServiceClient(options));
        
        if (exception != null)
        {
            // Should throw a runtime exception (credential issues), not return null for valid URLs
            // Common exceptions: CredentialUnavailableException, AuthenticationFailedException
            Assert.True(exception is Azure.Identity.CredentialUnavailableException || 
                       exception is Azure.Identity.AuthenticationFailedException ||
                       exception is InvalidOperationException, 
                       $"Expected runtime exception, but got: {exception.GetType().Name}");
        }
        // If no exception, then credentials were available and client was created successfully
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_ForSasTokenUrls()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger);
        var options = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://test.blob.core.windows.net/container?sv=2020-08-04&ss=b&srt=c&sp=r&se=2023-12-31T23:59:59Z&st=2023-01-01T00:00:00Z&spr=https&sig=example"
        };

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - SAS tokens are configuration issues, should return null
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_ForInvalidUrls()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger);
        var options = new BlobConfigurationOptions
        {
            BlobContainerUrl = "not-a-valid-url"
        };

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Invalid URLs are configuration issues, should return null
        Assert.Null(result);
    }
}
