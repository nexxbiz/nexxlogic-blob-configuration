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
}