using Castle.Core.Logging;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddJsonBlob_ShouldThrowValidationException_WhenBlobConfigurationOptionsIsNotValid()
    {
        // Arrange
        var configurationBuilderMock = new Mock<IConfigurationBuilder>();
        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        // Act
        var method = () => configurationBuilderMock.Object.AddJsonBlob(config => { }, logger);

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

        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        // Act
        configurationBuilderMock.Object.AddJsonBlob(config =>
        {
            config.ConnectionString = "CONNECTION_STRING";
            config.ContainerName = "CONTAINER_NAME";
            config.BlobName = "BLOB_NAME";
        }, logger);

        // Assert
        configurationBuilderMock
            .Verify(_ => _.Add(It.IsAny<JsonConfigurationSource>()), Times.Once);
    }
}
