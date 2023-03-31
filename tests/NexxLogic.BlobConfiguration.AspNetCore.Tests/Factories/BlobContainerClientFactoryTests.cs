using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

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
        result.Name.Should().Be(blobConfig.ContainerName);
    }

    [Fact]
    public void GetBlobContainerClient_When_ContainerUrl_IsSpecified()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://fakestorageaccount.blob.core.windows.net/configuration?sig=asdjkhasdhasdjkhsakjd",
            ContainerName = "configuration"
        };
        var sut = new BlobContainerClientFactory(blobConfig);

        // Act
        var result = sut.GetBlobContainerClient();

        // Assert
        result.Name.Should().Be(blobConfig.ContainerName);
        result.Uri.ToString().Should().Be(blobConfig.BlobContainerUrl);
    }
}
