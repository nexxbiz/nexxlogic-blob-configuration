using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobClientFactoryTests
{
    [Fact]
    public void GetBlobClient()
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

        // Assert
        Assert.Equal(blobContainerName, result.BlobContainerName);
        Assert.Equal(blobName, result.Name);
    }
}