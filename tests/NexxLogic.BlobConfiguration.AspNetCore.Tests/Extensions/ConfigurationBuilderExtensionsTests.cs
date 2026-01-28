using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using Azure.Core;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Extensions;

public class BlobConfigurationBuilderExtensionsTests
{
    private readonly IConfigurationBuilder _configurationBuilderMock = Substitute.For<IConfigurationBuilder>();
    private readonly ILogger<BlobFileProvider> _logger = NullLogger<BlobFileProvider>.Instance;
    private readonly TokenCredential _mockCredential = Substitute.For<TokenCredential>();
    
    // Test constants
    private const string TestConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net";
    private const string TestContainerName = "test-container";
    private const string TestBlobName = "appsettings.json";

    [Theory]
    [MemberData(nameof(GetInvalidConfigurationCases))]
    public void AddJsonBlob_ShouldThrowArgumentException_WhenBlobConfigurationIsInvalid(
        Action<BlobConfigurationOptions> configAction,
        string expectedErrorFragment)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _configurationBuilderMock.AddJsonBlob(configAction, _logger, _mockCredential));
        
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains(expectedErrorFragment, exception.Message);
        
        _configurationBuilderMock.DidNotReceiveWithAnyArgs().Add(Arg.Any<IConfigurationSource>());
    }

    [Fact]
    public void AddJsonBlob_ShouldAddJsonConfigurationSource_WhenConfigurationIsValid()
    {
        // Arrange
        JsonConfigurationSource? capturedSource = null;
        _configurationBuilderMock.Add(Arg.Do<IConfigurationSource>(source => capturedSource = source as JsonConfigurationSource));

        // Act
        _configurationBuilderMock.AddJsonBlob(GetValidConfiguration(), _logger, _mockCredential);

        // Assert
        _configurationBuilderMock.Received(1).Add(Arg.Any<IConfigurationSource>());
        
        Assert.NotNull(capturedSource);
        Assert.IsType<BlobFileProvider>(capturedSource.FileProvider);
        Assert.Equal("appsettings.json", capturedSource.Path);
        Assert.False(capturedSource.ReloadOnChange); // Should be explicit about expected behavior
    }

    public static IEnumerable<object[]> GetInvalidConfigurationCases()
    {
        yield return
        [
            new Action<BlobConfigurationOptions>(_ => { }), 
            "ConnectionString"
        ];
        
        yield return
        [
            new Action<BlobConfigurationOptions>(config => config.ConnectionString = TestConnectionString), 
            "ContainerName"
        ];
        
        yield return
        [
            new Action<BlobConfigurationOptions>(config => 
            {
                config.ConnectionString = TestConnectionString;
                config.ContainerName = TestContainerName;
            }), 
            "BlobName"
        ];
        
        yield return
        [
            new Action<BlobConfigurationOptions>(config => 
            {
                config.ConnectionString = "";
                config.ContainerName = TestContainerName;
                config.BlobName = TestBlobName;
            }), 
            "ConnectionString"
        ];}

    private static Action<BlobConfigurationOptions> GetValidConfiguration() =>
        config =>
        {
            config.ConnectionString = TestConnectionString;
            config.ContainerName = TestContainerName;
            config.BlobName = TestBlobName;
        };
}