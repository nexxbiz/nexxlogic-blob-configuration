using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;
using Azure.Storage.Blobs;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobContainerClientFactoryTests
{
    [Fact]
    public void GetBlobContainerClient_When_ConnectionString_IsSpecified()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "configuration",
            BlobName = ""
        };
        var sut = new BlobContainerClientFactory(blobConfig);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.Equal(blobConfig.ContainerName, result.Name);
    }

    [Fact]
    public void GetBlobContainerClient_When_ContainerUrl_IsSpecified()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://fakestorageaccount.blob.core.windows.net/configuration",
            ContainerName = "configuration"
        };
        var sut = new BlobContainerClientFactory(blobConfig);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        Assert.Equal(blobConfig.ContainerName, result.Name);
        Assert.Equal(blobConfig.BlobContainerUrl, result.Uri.ToString());
    }

    [Fact]
    public void GetBlobContainerClient_When_ContainerUrl_WithoutSAS_UsesCredentials()
    {
        // Arrange - URL without SAS token (no query string)
        var blobConfig = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://mystorageaccount.blob.core.windows.net/configuration",
            ContainerName = "configuration"
        };

        var mockBlobServiceClient = Substitute.For<BlobServiceClient>();
        var mockContainerClient = Substitute.For<BlobContainerClient>();

        mockBlobServiceClient.GetBlobContainerClient("configuration")
            .Returns(mockContainerClient);

        var blobServiceClientFactory = Substitute.For<IBlobServiceClientFactory>();
        blobServiceClientFactory.CreateBlobServiceClient(blobConfig)
            .Returns(mockBlobServiceClient);

        var sut = new BlobContainerClientFactory(blobConfig, blobServiceClientFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert - Should use authenticated client via BlobServiceClientFactory
        Assert.Same(mockContainerClient, result);
        blobServiceClientFactory.Received(1).CreateBlobServiceClient(blobConfig);
        mockBlobServiceClient.Received(1).GetBlobContainerClient("configuration");
    }

    [Fact]
    public void GetBlobContainerClient_When_ContainerUrl_WithSAS_UsesAnonymous()
    {
        // Arrange - URL with SAS token (has query string)
        var blobConfig = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://mystorageaccount.blob.core.windows.net/configuration?sv=2021-06-08&se=2023-12-31T23:59:59Z&sr=c&sp=rl&sig=...",
            ContainerName = "configuration"
        };

        var blobServiceClientFactory = Substitute.For<IBlobServiceClientFactory>();
        var sut = new BlobContainerClientFactory(blobConfig, blobServiceClientFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert - Should NOT use BlobServiceClientFactory for SAS URLs
        Assert.NotNull(result);
        Assert.Equal("configuration", result.Name);
        blobServiceClientFactory.DidNotReceive().CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>());
    }

    [Fact]
    public void GetBlobContainerClient_When_ContainerUrl_WithoutSAS_AndNoCredentials_FallsBackToAnonymous()
    {
        // Arrange - URL without SAS token but no credentials available
        var blobConfig = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://mystorageaccount.blob.core.windows.net/configuration",
            ContainerName = "configuration"
        };

        var blobServiceClientFactory = Substitute.For<IBlobServiceClientFactory>();
        blobServiceClientFactory.CreateBlobServiceClient(blobConfig)
            .Returns((BlobServiceClient?)null); // Simulate credential failure

        var sut = new BlobContainerClientFactory(blobConfig, blobServiceClientFactory);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert - Should fallback to anonymous (may fail at runtime for private containers)
        Assert.NotNull(result);
        Assert.Equal("configuration", result.Name);
        blobServiceClientFactory.Received(1).CreateBlobServiceClient(blobConfig);
    }
}