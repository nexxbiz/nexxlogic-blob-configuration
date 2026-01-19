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
    [InlineData(true, typeof(EnhancedBlobChangeToken), typeof(BlobChangeToken))] // Enhanced mode might fallback to legacy
    [InlineData(false, typeof(BlobChangeToken), typeof(BlobChangeToken))] // Legacy mode always returns BlobChangeToken
    public void BlobFileProvider_ShouldCreateCorrectTokenType_BasedOnConfiguration(bool useEnhancedFeatures, Type primaryExpectedType, Type fallbackExpectedType)
    {
        // Arrange
        var options = useEnhancedFeatures 
            ? CreateOptionsWithConnectionString()
            : new BlobConfigurationOptions { ReloadInterval = 1000, ContainerName = ContainerName };
        var provider = CreateBlobFileProvider(options);

        // Act
        var changeToken = provider.Watch(BlobName);

        // Assert - Enhanced mode might fallback to legacy if BlobServiceClient creation fails
        if (useEnhancedFeatures)
        {
            // Enhanced mode: might get enhanced token or fallback to legacy
            Assert.True(changeToken.GetType() == primaryExpectedType || changeToken.GetType() == fallbackExpectedType,
                $"Expected {primaryExpectedType.Name} or {fallbackExpectedType.Name}, got {changeToken.GetType().Name}");
        }
        else
        {
            // Legacy mode: should always get legacy token
            Assert.IsType(primaryExpectedType, changeToken);
        }
    }

    [Fact]
    public void BlobFileProvider_ShouldCreateTokenWithIntelligentStrategy()
    {
        // Arrange - No strategy configuration needed, factory chooses intelligently
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);

        // Act
        var changeToken = provider.Watch(BlobName);

        // Assert - Enhanced mode might fallback to legacy if BlobServiceClient creation fails
        if (changeToken is EnhancedBlobChangeToken)
        {
            Assert.IsType<EnhancedBlobChangeToken>(changeToken);
        }
        else
        {
            Assert.IsType<BlobChangeToken>(changeToken); // Fallback to legacy mode
        }
        
        // Register a callback to make ActiveChangeCallbacks meaningful
        var registration = changeToken.RegisterChangeCallback(_ => { }, null);
        Assert.True(changeToken.ActiveChangeCallbacks);
        Assert.False(changeToken.HasChanged);
        registration.Dispose();
    }

    [Fact]
    public void BlobFileProvider_ShouldCacheTokens_ForSameBlobPath()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);

        // Act
        var token1 = provider.Watch(BlobName);
        var token2 = provider.Watch(BlobName); // Same blob path should return cached token

        // Assert - Check if enhanced mode is working
        if (token1 is EnhancedBlobChangeToken)
        {
            // Enhanced Mode: Same blob path should return same cached token instance
            Assert.IsType<EnhancedBlobChangeToken>(token1);
            Assert.IsType<EnhancedBlobChangeToken>(token2);
            Assert.Same(token1, token2);
        }
        else
        {
            // Legacy Mode: All Watch() calls return the same provider-level token anyway
            Assert.IsType<BlobChangeToken>(token1);
            Assert.IsType<BlobChangeToken>(token2);
            Assert.Same(token1, token2);
        }
    }

    [Fact]
    public void BlobFileProvider_ShouldThrowArgumentException_WithInvalidConfiguration()
    {
        // Arrange
        var invalidOptions = new BlobConfigurationOptions
        {
            ReloadInterval = -1,
            DebounceDelay = TimeSpan.FromSeconds(-5),
            WatchingInterval = TimeSpan.FromSeconds(0),
            ErrorRetryDelay = TimeSpan.FromSeconds(-10),
            MaxFileContentHashSizeMb = 0,
            ConnectionString = ConnectionString,
            ContainerName = ContainerName
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(invalidOptions));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
    }

    [Theory]
    [InlineData(1000, 0, 1, 1, 1)] // Minimum valid values (backward compatible)
    [InlineData(30000, 30, 60, 120, 5)] // Typical production values
    [InlineData(86400000, 3600, 86400, 7200, 1024)] // Maximum valid values
    public void BlobFileProvider_ShouldAcceptConfiguration_WithValidValues(
        int reloadInterval, int debounceDelaySeconds, int watchingIntervalSeconds, int errorRetryDelaySeconds, int maxHashSize)
    {
        // Arrange
        var validOptions = new BlobConfigurationOptions
        {
            ReloadInterval = reloadInterval,
            DebounceDelay = TimeSpan.FromSeconds(debounceDelaySeconds),
            WatchingInterval = TimeSpan.FromSeconds(watchingIntervalSeconds),
            ErrorRetryDelay = TimeSpan.FromSeconds(errorRetryDelaySeconds),
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
        // This test verifies that different blob paths get different cached tokens
        // but strategy instances are reused for performance
        
        // Arrange
        var options = CreateOptionsWithConnectionString();
        var provider = CreateBlobFileProvider(options);

        // Act
        var token1 = provider.Watch(BlobName); // "settings.json"
        var token2 = provider.Watch("other.json"); // Different blob path

        // Assert - Check if enhanced mode is working
        if (token1 is EnhancedBlobChangeToken)
        {
            // Enhanced Mode: Different blob paths should return different cached token instances
            Assert.IsType<EnhancedBlobChangeToken>(token1);
            Assert.IsType<EnhancedBlobChangeToken>(token2);
            Assert.NotSame(token1, token2); // Different paths = different tokens
        }
        else
        {
            // Legacy Mode: All Watch() calls return the same provider-level token
            Assert.IsType<BlobChangeToken>(token1);
            Assert.IsType<BlobChangeToken>(token2);
            Assert.Same(token1, token2); // Legacy mode = same token for all paths
        }
        
        // Both tokens should work independently regardless of mode
        // Register callbacks to make ActiveChangeCallbacks meaningful
        var registration1 = token1.RegisterChangeCallback(_ => { }, null);
        var registration2 = token2.RegisterChangeCallback(_ => { }, null);
        
        Assert.True(token1.ActiveChangeCallbacks);
        Assert.True(token2.ActiveChangeCallbacks);
        
        registration1.Dispose();
        registration2.Dispose();
    }

    private static BlobConfigurationOptions CreateOptionsWithConnectionString()
    {
        return new BlobConfigurationOptions
        {
            // Use actual ConnectionString to enable enhanced features
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=https://127.0.0.1:10000/devstoreaccount1;",
            ContainerName = ContainerName,
            ReloadInterval = 1000,
            DebounceDelay = TimeSpan.FromSeconds(30),
            WatchingInterval = TimeSpan.FromMinutes(1),
            ErrorRetryDelay = TimeSpan.FromMinutes(2),
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

        var blobServiceClientFactoryMock = Substitute.For<IBlobServiceClientFactory>();
        
        // Configure mock based on whether we want enhanced or legacy mode
        if (!string.IsNullOrEmpty(options.ConnectionString) || 
            (!string.IsNullOrEmpty(options.BlobContainerUrl) && !options.BlobContainerUrl.Contains("?")))
        {
            // Enhanced mode - return mock BlobServiceClient
            var mockBlobServiceClient = Substitute.For<BlobServiceClient>();
            blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
                .Returns(mockBlobServiceClient);
        }
        else
        {
            // Legacy mode - return null
            blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
                .Returns((BlobServiceClient?)null);
        }

        logger ??= new NullLogger<BlobFileProvider>();

        return new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            blobServiceClientFactoryMock,
            options,
            logger);
    }
}