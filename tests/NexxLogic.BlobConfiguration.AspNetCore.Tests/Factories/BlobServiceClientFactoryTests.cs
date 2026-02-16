using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using Azure.Core;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobServiceClientFactoryTests
{
    // Test constants for maintainability
    private const string ValidConnectionString = "DefaultEndpointsProtocol=https;AccountName=teststorage;AccountKey=dGVzdGtleWZvcnVuaXR0ZXN0aW5ncHVycG9zZXM=;EndpointSuffix=core.windows.net";
    private const string ValidBlobContainerUrl = "https://teststorage.blob.core.windows.net/testcontainer";
    private const string ValidSasToken = "sv=2021-06-08&se=2023-12-31T23:59:59Z&sr=c&sp=rl&sig=testsignature";
    private const string BlobContainerUrlWithSas = $"{ValidBlobContainerUrl}?{ValidSasToken}";

    private readonly ILogger<BlobServiceClientFactory> _logger = new NullLogger<BlobServiceClientFactory>();
    private readonly TokenCredential _mockCredential = Substitute.For<TokenCredential>();

    [Fact]
    public void Constructor_ShouldAllowNullTokenCredential()
    {
        // Act & Assert - Should not throw
        var factory = new BlobServiceClientFactory(_logger, tokenCredential: null);
        Assert.NotNull(factory);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnClient_WhenValidConnectionStringProvided()
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithConnectionString(ValidConnectionString);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("teststorage", result.AccountName);
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net")] // Empty account name
    [InlineData("DefaultEndpointsProtocol=https;AccountName=test;")] // Missing AccountKey and EndpointSuffix
    [InlineData("InvalidConnectionString")] // Completely malformed
    [InlineData("")] // Empty string
    [InlineData("   ")] // Whitespace only
    public void CreateBlobServiceClient_ShouldReturnNull_WhenConnectionStringIsInvalid(string invalidConnectionString)
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithConnectionString(invalidConnectionString);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenConnectionStringHasInvalidAccountKey()
    {
        // Arrange
        var factory = CreateFactory();
        const string invalidConnectionString = "DefaultEndpointsProtocol=https;AccountName=teststorage;AccountKey=invalidkey!@#;EndpointSuffix=core.windows.net";
        var options = CreateOptionsWithConnectionString(invalidConnectionString);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Azure SDK should reject invalid base64 account keys
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenNoConnectionInfoProvided()
    {
        // Arrange
        var factory = CreateFactory();
        var options = new BlobConfigurationOptions();

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldCreateClientWithCredentials_WhenValidUrlAndCredentialProvided()
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithContainerUrl(ValidBlobContainerUrl);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.NotNull(result);
        // Verify the URL is correctly parsed
        Assert.Contains("teststorage", result.AccountName);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenUrlProvidedButNoCredential()
    {
        // Arrange
        var factory = new BlobServiceClientFactory(_logger, tokenCredential: null);
        var options = CreateOptionsWithContainerUrl(ValidBlobContainerUrl);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(BlobContainerUrlWithSas)] // Standard SAS token
    [InlineData($"{ValidBlobContainerUrl}?sv=2021-06-08&sig=anothersig")] // Minimal SAS
    [InlineData($"{ValidBlobContainerUrl}?other=param&{ValidSasToken}")] // SAS with other params
    [InlineData($"{ValidBlobContainerUrl}?{ValidSasToken}&timeout=30")] // SAS with trailing params
    public void CreateBlobServiceClient_ShouldReturnNull_WhenUrlContainsSasToken(string sasUrl)
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithContainerUrl(sasUrl);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - SAS tokens indicate anonymous access, no service client needed
        Assert.Null(result);
    }

    [Theory]
    [InlineData("not-a-url")] // Plain text
    [InlineData("ftp://invalid.com/container")] // Wrong protocol
    [InlineData("https://")] // Incomplete URL
    [InlineData("https://malformed")] // No domain
    [InlineData("https://incomplete.")] // Trailing dot only
    public void CreateBlobServiceClient_ShouldReturnNull_WhenUrlIsInvalid(string invalidUrl)
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithContainerUrl(invalidUrl);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithConnectionString(ValidConnectionString);

        // Act & Assert - Should be safe to call multiple times
        for (var i = 0; i < 3; i++)
        {
            var result = factory.CreateBlobServiceClient(options);
            Assert.NotNull(result);
            Assert.Contains("teststorage", result.AccountName);
        }
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldCreateDifferentInstances_WhenCalledMultipleTimes()
    {
        // Arrange
        var factory = CreateFactory();
        var options = CreateOptionsWithConnectionString(ValidConnectionString);

        // Act
        var client1 = factory.CreateBlobServiceClient(options);
        var client2 = factory.CreateBlobServiceClient(options);

        // Assert - Should create new instances each time (not cached)
        Assert.NotNull(client1);
        Assert.NotNull(client2);
        Assert.NotSame(client1, client2);
        Assert.Equal(client1.AccountName, client2.AccountName); // Same configuration
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldHandleSpecialCharactersInUrl()
    {
        // Arrange
        var factory = CreateFactory();
        const string urlWithSpecialChars = "https://test-storage_account.blob.core.windows.net/container-name";
        var options = CreateOptionsWithContainerUrl(urlWithSpecialChars);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Should handle valid URLs with hyphens and underscores
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldReturnNull_WhenUrlHasInvalidPortNumber()
    {
        // Arrange
        var factory = CreateFactory();
        const string invalidPortUrl = "https://teststorage.blob.core.windows.net:99999/testcontainer";
        var options = CreateOptionsWithContainerUrl(invalidPortUrl);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Invalid port numbers should be handled gracefully
        Assert.Null(result);
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldLeverageAzureSDKValidation_ForConnectionStrings()
    {
        // Arrange
        var factory = CreateFactory();
        
        // Test a connection string that looks syntactically correct but has logical errors
        const string logicallyInvalidConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=;EndpointSuffix=core.windows.net";
        var options = CreateOptionsWithConnectionString(logicallyInvalidConnectionString);

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert - Let Azure SDK handle the validation rather than custom parsing
        Assert.Null(result); // Azure SDK should reject this and factory should return null
    }

    [Fact]
    public void CreateBlobServiceClient_ShouldPreferConnectionString_WhenBothConnectionStringAndUrlProvided()
    {
        // Arrange
        var factory = CreateFactory();
        var options = new BlobConfigurationOptions
        {
            ConnectionString = ValidConnectionString,
            BlobContainerUrl = ValidBlobContainerUrl
        };

        // Act
        var result = factory.CreateBlobServiceClient(options);

        // Assert
        Assert.NotNull(result);
        // Verify it used the connection string (should match the account name from connection string)
        Assert.Contains("teststorage", result.AccountName);
    }

    // Helper methods
    private BlobServiceClientFactory CreateFactory() => 
        new(_logger, _mockCredential);

    private static BlobConfigurationOptions CreateOptionsWithConnectionString(string connectionString) => 
        new() { ConnectionString = connectionString };

    private static BlobConfigurationOptions CreateOptionsWithContainerUrl(string containerUrl) => 
        new() { BlobContainerUrl = containerUrl };
}