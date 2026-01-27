using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using Azure.Identity;
using Microsoft.Extensions.Configuration.Json;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddJsonBlob_ShouldThrowArgumentException_WhenBlobConfigurationOptionsIsNotValid()
    {
        // Arrange
        var configurationBuilderMock = Substitute.For<IConfigurationBuilder>();
        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            configurationBuilderMock.AddJsonBlob(config => { }, logger, new DefaultAzureCredential()));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        configurationBuilderMock
            .DidNotReceive()
            .Add(Substitute.For<IConfigurationSource>());
    }

    [Fact]
    public void AddJsonBlob_ShouldAddConfigurationSource_WhenBlobConfigurationBuilderOptionsIsValid()
    {
        // Arrange
        var configurationBuilderMock = Substitute.For<IConfigurationBuilder>();

        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        // Act
        configurationBuilderMock.AddJsonBlob(config =>
        {
            config.ConnectionString = "CONNECTION_STRING";
            config.ContainerName = "CONTAINER_NAME";
            config.BlobName = "BLOB_NAME";
        }, 
        logger,
        new DefaultAzureCredential());

        // Assert
        var calls = configurationBuilderMock.ReceivedCalls();

        var addCall = calls.Single(x =>
        {
            var methodInfo = x.GetMethodInfo();
            return methodInfo.Name == nameof(IConfigurationBuilder.Add);
        });
        var arguments = addCall.GetArguments();
        var argument = arguments.Single();
        Assert.Equal(typeof(JsonConfigurationSource), argument?.GetType());
    }
}