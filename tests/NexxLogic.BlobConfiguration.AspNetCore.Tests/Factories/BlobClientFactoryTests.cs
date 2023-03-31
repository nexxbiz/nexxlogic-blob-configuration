using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobClientFactoryTests
{
    [Fact]
    public void GetBlobClient()
    {
        // Arrange
        var blobName = "settings.json";
        var blobContainerName = "config";
        var blobContainerFactory = new Mock<IBlobContainerClientFactory>();

        var blobClient = new Mock<BlobClient>();
        blobClient.SetupGet(x => x.BlobContainerName).Returns(blobContainerName);
        blobClient.SetupGet(x => x.Name).Returns(blobName);

        var blobContainerClient = new Mock<BlobContainerClient>();
        blobContainerClient.SetupGet(x => x.Name).Returns(blobContainerName);
        blobContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(blobClient.Object);
        blobContainerFactory.Setup(x => x.GetBlobContainerClient())
            .Returns(blobContainerClient.Object);

        var sut = new BlobClientFactory(blobContainerFactory.Object);

        // Act
        var result = sut.GetBlobClient(blobName);

        // Assert
        result.BlobContainerName.Should().Be(blobContainerName);
        result.Name.Should().Be(blobName);
    }
}
