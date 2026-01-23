using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobClientFactoryTests
{
    [Fact]
    public void GetBlobClient_ReturnsClientFromContainerFactory()
    {
        // Arrange
        const string blobName = "settings.json";
        const string blobContainerName = "config";
        var blobContainerFactory = Substitute.For<IBlobContainerClientFactory>();

        var blobClient = Substitute.For<BlobClient>();
        blobClient
            .BlobContainerName
            .Returns(blobContainerName);

        blobClient
            .Name
            .Returns(blobName);

        var blobContainerClient = Substitute.For<BlobContainerClient>();
        blobContainerClient.Name.Returns(blobContainerName);
        blobContainerClient
            .GetBlobClient(blobName)
            .Returns(blobClient);

        blobContainerFactory
            .GetBlobContainerClient()
            .Returns(blobContainerClient);

        var sut = new BlobClientFactory(blobContainerFactory);

        // Act
        var result = sut.GetBlobClient(blobName);

        // Assert - Verify the factory makes the correct method calls in sequence
        blobContainerFactory.Received(1).GetBlobContainerClient();
        blobContainerClient.Received(1).GetBlobClient(blobName);
        
        // Verify the result is not null (the actual BlobClient instance from the mock)
        Assert.NotNull(result);
        Assert.Same(blobClient, result);
    }
}