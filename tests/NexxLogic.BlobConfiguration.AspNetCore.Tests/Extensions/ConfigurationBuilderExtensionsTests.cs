using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddJsonBlob_ShouldThrowValidationException_WhenBlobConfigurationOptionsIsNotValid()
    {
        // Arrange
        var configurationBuilderMock = new Mock<IConfigurationBuilder>();

        // Act
        var method = () => configurationBuilderMock.Object.AddJsonBlob(config => { });

        // Assert
        method.Should().Throw<ValidationException>();
        configurationBuilderMock
            .Verify(_ => _.Add(It.IsAny<IConfigurationSource>()), Times.Never);
    }

    [Fact]
    public void AddJsonBlob_ShouldAddConfigurationSource_WhenBlobConfigurationBuilderOptionsIsValid()
    {
        // Arrange
        var configurationBuilderMock = new Mock<IConfigurationBuilder>();

        // Act
        configurationBuilderMock.Object.AddJsonBlob(config =>
        {
            config.ConnectionString = "CONNECTION_STRING";
            config.ContainerName = "CONTAINER_NAME";
            config.BlobName = "BLOB_NAME";
        });

        // Assert
        configurationBuilderMock
            .Verify(_ => _.Add(It.IsAny<JsonConfigurationSource>()), Times.Once);
    }
}
