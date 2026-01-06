using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class EnhancedBlobChangeDetectionTests
{
    private const string BlobName = "settings.json";
    private const string ContainerName = "configuration";
    private const string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net";
    
    [Theory]
    [InlineData(true, typeof(EnhancedBlobChangeToken))]
    [InlineData(false, typeof(BlobChangeToken))]
    public void BlobFileProvider_ShouldCreateCorrectTokenType_BasedOnConfiguration(bool useEnhancedFeatures, Type expectedTokenType)
    {
        // Arrange
        var options = useEnhancedFeatures 
            ? CreateOptionsWithConnectionString()
            : new BlobConfigurationOptions { ReloadInterval = 1000, ContainerName = ContainerName };
        var provider = CreateBlobFileProvider(options);

        // Act
        var changeToken = provider.Watch(BlobName);

        // Assert
        Assert.IsType(expectedTokenType, changeToken);
    }

    [Fact]
    public void BlobFileProvider_ShouldCreateTokenWithIntelligentStrategy()
    {
        // Arrange - No strategy configuration needed, factory chooses intelligently
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);

        // Act
        var changeToken = provider.Watch(BlobName);

        // Assert
        Assert.IsType<EnhancedBlobChangeToken>(changeToken);
        Assert.True(changeToken.ActiveChangeCallbacks);
        Assert.False(changeToken.HasChanged);
    }

    [Fact]
    public void BlobFileProvider_ShouldCreateFreshTokens_OnEachWatchCall()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);

        // Act
        var token1 = provider.Watch(BlobName);
        var token2 = provider.Watch(BlobName);

        // Assert
        Assert.IsType<EnhancedBlobChangeToken>(token1);
        Assert.IsType<EnhancedBlobChangeToken>(token2);
        Assert.NotSame(token1, token2);
    }

    [Fact]
    public void BlobFileProvider_ShouldThrowArgumentException_WithInvalidConfiguration()
    {
        // Arrange
        var invalidOptions = new BlobConfigurationOptions
        {
            ReloadInterval = -1,
            DebounceDelaySeconds = -5,
            WatchingIntervalSeconds = 0,
            ErrorRetryDelaySeconds = -10,
            MaxFileContentHashSizeMb = 0,
            ConnectionString = ConnectionString,
            ContainerName = ContainerName
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(invalidOptions));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
    }

    [Theory]
    [InlineData(1, 0, 1, 1, 1)] // Minimum valid values (backward compatible)
    [InlineData(30000, 30, 60, 120, 5)] // Typical production values
    [InlineData(86400000, 3600, 86400, 7200, 1024)] // Maximum valid values
    public void BlobFileProvider_ShouldAcceptConfiguration_WithValidValues(
        int reloadInterval, int debounceDelay, int watchingInterval, int errorRetryDelay, int maxHashSize)
    {
        // Arrange
        var validOptions = new BlobConfigurationOptions
        {
            ReloadInterval = reloadInterval,
            DebounceDelaySeconds = debounceDelay,
            WatchingIntervalSeconds = watchingInterval,
            ErrorRetryDelaySeconds = errorRetryDelay,
            MaxFileContentHashSizeMb = maxHashSize,
            ConnectionString = ConnectionString,
            ContainerName = ContainerName
        };

        // Act & Assert
        var exception = Record.Exception(() => CreateBlobFileProvider(validOptions));
        Assert.Null(exception);
    }

    [Fact]
    public void BlobFileProvider_ShouldFallbackToLegacy_WhenBlobContainerUrlContainsSasToken()
    {
        // Arrange
        var options = new BlobConfigurationOptions
        {
            BlobContainerUrl = "https://test.blob.core.windows.net/container?sv=2021-06-08&ss=b&srt=sco&sp=rwl&se=2023-12-31T23:59:59Z&st=2023-01-01T00:00:00Z&spr=https&sig=signature",
            ContainerName = ContainerName,
            ReloadInterval = 1000
        };
        var logger = Substitute.For<ILogger<BlobFileProvider>>();
        var provider = CreateBlobFileProvider(options, logger);

        // Act
        var changeToken = provider.Watch(BlobName);

        // Assert
        Assert.IsType<BlobChangeToken>(changeToken); // Should fallback to legacy
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("BlobContainerUrl contains a SAS token")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [InlineData("BlobFileProvider")]
    [InlineData("EnhancedBlobChangeToken")]
    public void DisposableObjects_ShouldDisposeCorrectly_WithoutExceptions(string objectType)
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);
        
        // Act & Assert
        if (objectType == "BlobFileProvider")
        {
            // Create tokens to ensure provider has state before disposal
            provider.Watch(BlobName);
            provider.Watch("other.json");
            
            var exception = Record.Exception(() => provider.Dispose());
            Assert.Null(exception);
            
            Assert.Throws<ObjectDisposedException>(() => provider.Watch("test.json"));
        }
        else // EnhancedBlobChangeToken
        {
            var token = provider.Watch(BlobName) as EnhancedBlobChangeToken;
            
            var exception = Record.Exception(() => token?.Dispose());
            Assert.Null(exception);
        }
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldRegisterCallbacks_Successfully()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);
        var token = provider.Watch(BlobName);
        var callbackExecuted = false;

        // Act
        var registration = token.RegisterChangeCallback(_ => { callbackExecuted = true; }, null);

        // Assert
        Assert.NotNull(registration);
        Assert.IsAssignableFrom<IDisposable>(registration);
        Assert.False(callbackExecuted); // Should not execute immediately
    }

    [Theory]
    [InlineData("")]
    [InlineData("subfolder/")]
    [InlineData("deep/nested/path/")]
    public void BlobFileProvider_ShouldHandlePrefixes_Correctly(string prefix)
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        options.Prefix = prefix;
        var provider = CreateBlobFileProvider(options);

        // Act
        var exception = Record.Exception(() => provider.Watch(BlobName));
        var token = provider.Watch(BlobName);

        // Assert
        Assert.Null(exception);
        Assert.IsType<EnhancedBlobChangeToken>(token); // Enhanced features now working with BlobContainerUrl
    }

    [Fact]
    public void BlobFileProvider_ShouldReuseStrategyInstance_AcrossMultipleTokens()
    {
        // This test verifies the strategy optimization where instances are reused
        // We can't directly test instance reuse, but we can verify behavior is consistent
        
        // Arrange
        var options = CreateOptionsWithConnectionString();
        // Test removed - factory configuration no longer needed as strategy selection is intelligent and internal
        var provider = CreateBlobFileProvider(options);

        // Act
        var token1 = provider.Watch(BlobName);
        var token2 = provider.Watch("other.json");

        // Assert
        Assert.IsType<EnhancedBlobChangeToken>(token1); // Enhanced features now working with BlobContainerUrl
        Assert.IsType<EnhancedBlobChangeToken>(token2); // Enhanced features now working with BlobContainerUrl
        // Both tokens should work independently despite sharing strategy instance
        Assert.True(token1.ActiveChangeCallbacks);
        Assert.True(token2.ActiveChangeCallbacks);
    }

    private static BlobConfigurationOptions CreateOptionsWithConnectionString()
    {
        return new BlobConfigurationOptions
        {
            // Use BlobContainerUrl without SAS token to enable enhanced features
            BlobContainerUrl = "https://testaccount.blob.core.windows.net/configuration",
            ContainerName = ContainerName,
            ReloadInterval = 1000,
            DebounceDelaySeconds = 30,
            WatchingIntervalSeconds = 60,
            ErrorRetryDelaySeconds = 120,
            MaxFileContentHashSizeMb = 5,
            // Factory will intelligently choose strategy based on configuration
        };
    }

    private static BlobFileProvider CreateBlobFileProvider(BlobConfigurationOptions options, ILogger<BlobFileProvider>? logger = null)
    {
        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        var blobClientMock = Substitute.For<BlobClient>();
        blobClientFactoryMock
            .GetBlobClient(Arg.Any<string>())
            .Returns(blobClientMock);

        var blobContainerClientMock = Substitute.For<BlobContainerClient>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        blobContainerClientFactoryMock
            .GetBlobContainerClient()
            .Returns(blobContainerClientMock);

        logger ??= new NullLogger<BlobFileProvider>();

        return new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            options,
            logger);
    }
}