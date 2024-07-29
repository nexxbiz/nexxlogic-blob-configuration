using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobClientFactoryTests
{
    [Fact]
    public void GetBlobClient()
    {
        // Arrange
        var blobName = "settings.json";
        var blobContainerName = "config";
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
        result.BlobContainerName.Should().Be(blobContainerName);
        result.Name.Should().Be(blobName);
    }
}
