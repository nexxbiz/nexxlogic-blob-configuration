using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;
using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobContainerClientFactoryTests
{
    // Test constants
    private const string TestContainerName = "configuration";
    private const string TestConnectionString = "UseDevelopmentStorage=true";
    private const string TestStorageAccountUrl = "https://mystorageaccount.blob.core.windows.net";
    private const string TestContainerUrl = $"{TestStorageAccountUrl}/{TestContainerName}";
    private const string TestSasToken = "sv=2021-06-08&se=2023-12-31T23:59:59Z&sr=c&sp=rl&sig=testsignature";
    private const string TestContainerUrlWithSas = $"{TestContainerUrl}?{TestSasToken}";

    private readonly IBlobServiceClientFactory _mockBlobServiceFactory;
    private readonly BlobServiceClient _mockBlobServiceClient;
    private readonly BlobContainerClient _mockContainerClient;

    public BlobContainerClientFactoryTests()
    {
        _mockBlobServiceFactory = Substitute.For<IBlobServiceClientFactory>();
        _mockBlobServiceClient = Substitute.For<BlobServiceClient>();
        _mockContainerClient = Substitute.For<BlobContainerClient>();

        _mockBlobServiceClient
            .GetBlobContainerClient(TestContainerName)
            .Returns(_mockContainerClient);
    }

    [Fact]
    public void GetBlobContainerClient_ShouldCreateClientFromConnectionString_WhenConnectionStringIsProvided()
    {
        // Arrange
        var config = CreateConfigWithConnectionString();
        var sut = new BlobContainerClientFactory(config);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContainerName, result.Name);
    }

    [Fact]
    public void GetBlobContainerClient_ShouldCreateClientFromUrl_WhenContainerUrlIsProvided()
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(TestContainerUrl);
        var sut = new BlobContainerClientFactory(config);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContainerName, result.Name);
        Assert.Equal(TestContainerUrl, result.Uri.ToString());
    }

    [Theory]
    [InlineData(TestContainerUrlWithSas)] // SAS token in query string
    [InlineData($"{TestContainerUrl}?sv=2021-06-08&sig=anothersig")] // Minimal SAS parameters
    [InlineData($"{TestContainerUrl}?other=param&{TestSasToken}")] // SAS with other parameters
    public void GetBlobContainerClient_ShouldUseAnonymousAccess_WhenUrlContainsSasToken(
        string urlWithSas)
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(urlWithSas);
        var sut = new BlobContainerClientFactory(config, _mockBlobServiceFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContainerName, result.Name);
        
        // Verify SAS URLs bypass the service factory (anonymous access)
        _mockBlobServiceFactory.DidNotReceive().CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>());
    }

    [Theory]
    [InlineData($"{TestContainerUrl}?timeout=30&api-version=2021-06-08")] // API parameters
    [InlineData($"{TestContainerUrl}?restype=container&comp=list")] // Azure REST API parameters
    [InlineData($"{TestContainerUrl}?custom=value")] // Custom parameters
    public void GetBlobContainerClient_ShouldUseCredentials_WhenUrlHasNonSasQueryParameters(
        string urlWithNonSasParams)
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(urlWithNonSasParams);
        _mockBlobServiceFactory.CreateBlobServiceClient(config).Returns(_mockBlobServiceClient);
        var sut = new BlobContainerClientFactory(config, _mockBlobServiceFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.Same(_mockContainerClient, result);
        
        // Verify authenticated access is used for non-SAS URLs
        _mockBlobServiceFactory.Received(1).CreateBlobServiceClient(config);
        _mockBlobServiceClient.Received(1).GetBlobContainerClient(TestContainerName);
    }

    [Fact]
    public void GetBlobContainerClient_ShouldUseCredentials_WhenUrlHasNoQueryString()
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(TestContainerUrl);
        _mockBlobServiceFactory.CreateBlobServiceClient(config).Returns(_mockBlobServiceClient);
        var sut = new BlobContainerClientFactory(config, _mockBlobServiceFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.Same(_mockContainerClient, result);
        _mockBlobServiceFactory.Received(1).CreateBlobServiceClient(config);
        _mockBlobServiceClient.Received(1).GetBlobContainerClient(TestContainerName);
    }

    [Fact]
    public void GetBlobContainerClient_ShouldFallbackToAnonymousAccess_WhenCredentialCreationFails()
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(TestContainerUrl);
        _mockBlobServiceFactory.CreateBlobServiceClient(config).Returns((BlobServiceClient?)null);
        var sut = new BlobContainerClientFactory(config, _mockBlobServiceFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContainerName, result.Name);
        
        // Verify fallback behavior
        _mockBlobServiceFactory.Received(1).CreateBlobServiceClient(config);
        _mockBlobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    [Theory]
    [InlineData("")] // Empty container name
    [InlineData(null)] // Null container name
    [InlineData("   ")] // Whitespace container name
    public void GetBlobContainerClient_ShouldThrowInvalidOperationException_WhenContainerNameIsInvalidWithConnectionString(
        string? invalidContainerName)
    {
        // Arrange
        var config = new BlobConfigurationOptions
        {
            ConnectionString = TestConnectionString,
            ContainerName = invalidContainerName!,
            BlobName = "test.json"
        };

        var sut = new BlobContainerClientFactory(config);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => sut.GetBlobContainerClient());
        
        Assert.Contains("ContainerName must be provided when using ConnectionString", exception.Message);
    }

    [Fact]
    public void GetBlobContainerClient_ShouldSucceed_WhenValidContainerNameProvidedWithConnectionString()
    {
        // Arrange
        var config = new BlobConfigurationOptions
        {
            ConnectionString = TestConnectionString,
            ContainerName = TestContainerName,
            BlobName = "test.json"
        };

        var sut = new BlobContainerClientFactory(config);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContainerName, result.Name);
    }

    [Theory]
    [InlineData("not-a-url")] // Invalid URL format
    [InlineData("ftp://invalid.com/container")] // Non-HTTP protocol
    [InlineData("file:///local/path")] // File protocol
    [InlineData("http://")] // Incomplete URL
    [InlineData("https://")] // Incomplete HTTPS URL
    [InlineData("invalid://test.com/container")] // Unsupported protocol
    public void GetBlobContainerClient_ShouldThrowArgumentException_WhenBlobContainerUrlIsInvalid(
        string invalidUrl)
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(invalidUrl);
        var sut = new BlobContainerClientFactory(config);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => sut.GetBlobContainerClient());
        
        Assert.Contains("BlobContainerUrl must be a valid HTTP or HTTPS URI", exception.Message);
        Assert.Contains($"Provided value: '{invalidUrl}'", exception.Message);
        Assert.Contains("BlobContainerUrl", exception.ParamName);
    }

    [Theory]
    [InlineData("")] // Empty URL
    [InlineData("   ")] // Whitespace URL
    public void GetBlobContainerClient_ShouldThrowInvalidOperationException_WhenBlobContainerUrlIsEmptyOrWhitespace(
        string emptyUrl)
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(emptyUrl);
        var sut = new BlobContainerClientFactory(config);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => sut.GetBlobContainerClient());
        
        Assert.Contains("Either BlobContainerUrl or ConnectionString must be provided", exception.Message);
    }

    [Theory]
    [InlineData("http://test.com/container")] // Valid HTTP URL
    [InlineData("https://test.com/container")] // Valid HTTPS URL
    [InlineData("HTTP://TEST.COM/CONTAINER")] // Uppercase HTTP
    [InlineData("HTTPS://TEST.COM/CONTAINER")] // Uppercase HTTPS
    public void GetBlobContainerClient_ShouldAcceptValidHttpUrls_WhenProtocolIsHttpOrHttps(
        string validUrl)
    {
        // Arrange
        var config = CreateConfigWithContainerUrl(validUrl);
        var sut = new BlobContainerClientFactory(config);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.NotNull(result);
        // Note: The container name should be extracted from the URL path
    }

    [Fact]
    public void GetBlobContainerClient_ShouldThrowInvalidOperationException_WhenBothUrlAndConnectionStringAreNull()
    {
        // Arrange - No BlobContainerUrl and no ConnectionString
        var config = new BlobConfigurationOptions
        {
            BlobContainerUrl = null,
            ConnectionString = null,
            ContainerName = TestContainerName,
            BlobName = "test.json"
        };
        var sut = new BlobContainerClientFactory(config);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => sut.GetBlobContainerClient());
        
        Assert.Contains("Either BlobContainerUrl or ConnectionString must be provided", exception.Message);
    }

    private static BlobConfigurationOptions CreateConfigWithConnectionString() =>
        new()
        {
            ConnectionString = TestConnectionString,
            ContainerName = TestContainerName,
            BlobName = "appsettings.json"
        };

    private static BlobConfigurationOptions CreateConfigWithContainerUrl(string containerUrl) =>
        new()
        {
            BlobContainerUrl = containerUrl,
            ContainerName = TestContainerName,
            BlobName = "appsettings.json"
        };
}