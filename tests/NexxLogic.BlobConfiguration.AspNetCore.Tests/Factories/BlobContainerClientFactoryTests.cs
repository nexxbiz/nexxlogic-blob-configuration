using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobContainerClientFactoryTests
{
    [Fact]
    public void GetBlobContainerClient()
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
        var result = sut.GetBlobContainerClient("");

        // Assert
        result.Name.Should().Be(blobConfig.ContainerName);
    }
}
