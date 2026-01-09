using Castle.Core.Logging;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddJsonBlob_ShouldThrowValidationException_WhenBlobConfigurationOptionsIsNotValid()
    {
        // Arrange
        var configurationBuilderMock = Substitute.For<IConfigurationBuilder>();
        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        // Act
        var method = () => configurationBuilderMock.AddJsonBlob(config => { }, logger);

        // Assert
        method.Should().Throw<ValidationException>();
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
        logger);

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
