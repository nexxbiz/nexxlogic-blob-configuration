using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using Azure.Core;
using Microsoft.Extensions.Configuration.Json;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddJsonBlob_ShouldThrowArgumentException_WhenBlobConfigurationOptionsIsNotValid()
    {
        // Arrange
        var configurationBuilderMock = Substitute.For<IConfigurationBuilder>();
        var logger = NullLogger<BlobFileProvider>.Instance;
        var mockCredential = Substitute.For<TokenCredential>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            configurationBuilderMock.AddJsonBlob(config => { }, logger, mockCredential));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        configurationBuilderMock
            .DidNotReceiveWithAnyArgs()
            .Add(null!);
    }

    [Fact]
    public void AddJsonBlob_ShouldAddConfigurationSource_WhenBlobConfigurationBuilderOptionsIsValid()
    {
        // Arrange
        var configurationBuilderMock = Substitute.For<IConfigurationBuilder>();
        var logger = NullLogger<BlobFileProvider>.Instance;
        var mockCredential = Substitute.For<TokenCredential>();

        // Act
        configurationBuilderMock.AddJsonBlob(config =>
        {
            config.ConnectionString = "CONNECTION_STRING";
            config.ContainerName = "CONTAINER_NAME";
            config.BlobName = "BLOB_NAME";
        }, 
        logger,
        mockCredential);

        // Assert
        configurationBuilderMock.Received(1)
            .Add(Arg.Is<IConfigurationSource>(s => s is JsonConfigurationSource));
    }
}