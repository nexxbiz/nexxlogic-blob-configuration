namespace BlobConfigurationProvider.Tests.Factories;

public class BlobClientFactoryTests
{
    [Fact]
    public void GetBlobClient()
    {
        // Arrange
        var blobName = "settings.json";
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "configuration",
            BlobName = blobName
        };
        var sut = new BlobClientFactory(blobConfig);

        // Act
        var result = sut.GetBlobClient(blobName);

        // Assert
        result.BlobContainerName.Should().Be(blobConfig.ContainerName);
        result.Name.Should().Be(blobName);
    }
}
