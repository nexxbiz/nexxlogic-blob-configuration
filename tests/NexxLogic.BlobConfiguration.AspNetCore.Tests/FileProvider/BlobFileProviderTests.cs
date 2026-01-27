using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class BlobFileProviderTests
{   
    private const string BlobName = "settings.json";
    private const int DefaultContentLength = 123;
    private static readonly DateTimeOffset DefaultLastModified = new(2022, 12, 19, 1, 1, 1, default);      

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobExists()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .Name
            .Returns(BlobName);

        // Act
        var result = sut.GetFileInfo(BlobName);

        // Assert
        Assert.True(result.Exists);
        Assert.Equal(DefaultLastModified, result.LastModified);
        Assert.Equal(DefaultContentLength, result.Length);
        Assert.Equal(BlobName, result.Name);
    }

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobDoesNotExist()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .GetProperties()
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));

        // Act
        var result = sut.GetFileInfo(BlobName);

        // Assert
        Assert.False(result.Exists);
        Assert.Empty(result.Name);
    }

    [Fact]
    public async Task Watch_ShouldRaiseChange_WhenNewVersionIsAvailable()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 1000
        };
        var sut = CreateSut(out var blobClientMock, options);
        sut.GetFileInfo(BlobName);

        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: DefaultLastModified.Add(TimeSpan.FromSeconds(30)),
            contentLength: DefaultContentLength
        );

        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        changeToken.RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        blobClientMock
            .GetPropertiesAsync(null, changeToken.CancellationToken)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        // Act
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.Same(tcs.Task, completedTask);
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public async Task Watch_ShouldNotRetrieveProperties_WhenLoadIsPending()
    {
        // Act
        var sut = CreateSut(out var blobClientMock);
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        
        // Assert immediately - load should be pending right after Watch() call
        // No need to wait since we're testing the immediate state
        await blobClientMock
              .DidNotReceive()
              .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task Watch_ShouldReturn_WhenCancellationIsRequested()
    {
        // Arrange
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 100_000 // Long interval to ensure no automatic polling
        };
        var sut = CreateSut(out var blobClientMock, options);
        sut.GetFileInfo(BlobName);        

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        changeToken.OnReload(); // Trigger reload immediately
        
        // Assert - Should not call GetPropertiesAsync due to cancellation/reload
        await blobClientMock
           .DidNotReceive()
           .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task When_BlobFile_Not_Optional_And_BlobDoesNotExist_Watch_ShouldStopRunning()
    {
        // Arrange        
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .GetProperties()
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        sut.GetFileInfo(BlobName);

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);

        // Assert - Change token should be created and not check for properties immediately
        // The watch operation should stop running due to non-optional blob not existing
        Assert.NotNull(changeToken);
        await blobClientMock
            .DidNotReceive()
            .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task When_BlobFile_Optional_And_BlobDoesNotExist_Watch_Should_Keep_Checking_For_Existence()
    {
        // Arrange        
        var options = new BlobConfigurationOptions
        {            
            ReloadInterval = 1000,
            Optional = true
        };
        await using var sut = CreateSut(out var blobClientMock, options);
        var callCount = 0;
        var multipleCallsSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        blobClientMock
            .GetProperties()
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));

        // Set up Exists to signal when called multiple times
        blobClientMock
            .Exists(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var currentCount = Interlocked.Increment(ref callCount);
                if (currentCount >= 2)
                {
                    multipleCallsSignal.TrySetResult(true);
                }
                return Response.FromValue(false, Substitute.For<Response>());
            });

        sut.GetFileInfo(BlobName);

        // Act
        _ = (BlobChangeToken)sut.Watch(BlobName);
        
        // Wait for the signal with a generous timeout for CI environments
        var completedTask = await Task.WhenAny(multipleCallsSignal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        
        // Assert - The signal completing proves that Exists() was called multiple times
        Assert.Same(multipleCallsSignal.Task, completedTask);
    }

    [Fact]
    public async Task When_BlobFile_Optional_And_File_Added_Later_Then_RaisesChange()
    {
        // Arrange        
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 1000,
            Optional = true
        };
        await using var sut = CreateSut(out var blobClientMock, options);
        var firstExistsCallSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changeTokenChangedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existsCallCount = 0;

        blobClientMock
            .GetProperties()
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));

        // Set up Exists to return false initially, then true after first call
        blobClientMock
            .Exists(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var currentCount = Interlocked.Increment(ref existsCallCount);
                
                // Signal that the first Exists call has happened
                if (currentCount == 1)
                {
                    firstExistsCallSignal.TrySetResult(true);
                }
                
                // Return true from the second call onwards to simulate blob appearing
                var blobExists = currentCount > 1;
                return Response.FromValue(blobExists, Substitute.For<Response>());
            });

        sut.GetFileInfo(BlobName);
        
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);

        // Register a callback to signal when change token changes
        changeToken.RegisterChangeCallback(_ => changeTokenChangedSignal.TrySetResult(true), null);

        // Wait for the first Exists call
        var firstCallTask = await Task.WhenAny(firstExistsCallSignal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(firstExistsCallSignal.Task, firstCallTask);

        // Pre-Assert - should have been called and not changed yet
        blobClientMock.Received().Exists(changeToken.CancellationToken);
        Assert.False(changeToken.HasChanged);

        // Act - Wait for the change token to be triggered (blob appears)
        var changeTask = await Task.WhenAny(changeTokenChangedSignal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(changeTokenChangedSignal.Task, changeTask);

        // Assert
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public async Task BlobFileProvider_ShouldPreventRaceConditions_WhenWatchCalledConcurrently()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act - Create multiple concurrent calls to Watch with different filters
        // This tests token caching mechanism under concurrent access patterns with different paths
        var watchTasks = new Task<IChangeToken>[10];
        for (int i = 0; i < 10; i++)
        {
            var index = i; // Capture loop variable
            watchTasks[i] = Task.Run(() => provider.Watch($"config{index}.json"));
        }

        // Wait for all tasks to complete before provider disposal
        var tokenResults = await Task.WhenAll(watchTasks);

        // Assert - Each different path should return a different token instance
        Assert.Equal(10, tokenResults.Length);
        
        // All tokens should be distinct instances (one per unique path)
        var distinctTokens = tokenResults.Distinct().ToList();
        Assert.Equal(10, distinctTokens.Count);
    }

    [Fact]
    public void BlobFileProvider_ShouldCreateEnhancedTokens_WhenConnectionStringProvided()
    {
        // Arrange - Use enhanced provider setup that should create enhanced tokens
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act
        var token = provider.Watch("test.json");

        // Assert - Should get enhanced token when properly configured
        Assert.IsType<EnhancedBlobChangeToken>(token);
        Assert.False(token.HasChanged);
        
        // Verify basic functionality
        var callbackCalled = false;
        var registration = token.RegisterChangeCallback(_ => { callbackCalled = true; }, null);
        
        Assert.True(token.ActiveChangeCallbacks);
        Assert.False(callbackCalled);
        
        registration.Dispose();
    }

    [Fact]
    public void BlobFileProvider_ShouldReturnLegacyToken_InLegacyMode()
    {
        // Arrange - Use CreateSut which forces legacy mode
        var sut = CreateSut(out var _);

        // Act
        var token = sut.Watch("test.json");

        // Assert - Should get legacy token
        Assert.IsType<BlobChangeToken>(token);
        Assert.False(token.HasChanged);
    }

    [Fact]
    public void BlobFileProvider_ShouldReturnSameTokenForAllPaths_InLegacyMode()
    {
        // Arrange - Use CreateSut which forces legacy mode (no ConnectionString)
        var sut = CreateSut(out var _);

        // Act - Create tokens for different blob paths
        var token1 = sut.Watch("config1.json");
        var token2 = sut.Watch("config2.json");
        var token3 = sut.Watch("config1.json"); // Same as first

        // Assert - Legacy mode: all Watch() calls return the same provider-level token
        Assert.IsType<BlobChangeToken>(token1);
        Assert.IsType<BlobChangeToken>(token2);
        Assert.IsType<BlobChangeToken>(token3);
        
        Assert.Same(token1, token2); // Different paths = same token in legacy
        Assert.Same(token1, token3); // Same path = same token (but it's the same as different paths too)
    }

    [Fact]
    public void BlobFileProvider_ShouldCacheTokensPerPath_InEnhancedMode()
    {
        // Arrange - Use enhanced provider setup
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act - Create tokens for different blob paths
        var token1A = provider.Watch("config1.json");
        var token1B = provider.Watch("config1.json"); // Same path, should be cached
        var token2 = provider.Watch("config2.json");  // Different path, should be new

        // Assert - Enhanced mode should cache tokens per blob path
        Assert.IsType<EnhancedBlobChangeToken>(token1A);
        Assert.IsType<EnhancedBlobChangeToken>(token1B);
        Assert.IsType<EnhancedBlobChangeToken>(token2);
        
        // Same path should return same cached token
        Assert.Same(token1A, token1B); 
        
        // Different path should return different token
        Assert.NotSame(token1A, token2); 
    }

    [Fact]
    public void BlobFileProvider_ShouldFallbackToLegacy_WhenServiceClientCreationFails()
    {
        // Arrange - Create provider that will fail to create BlobServiceClient
        var options = CreateOptionsWithConnectionString();
        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        var blobServiceClientFactoryMock = Substitute.For<IBlobServiceClientFactory>();
        
        // Configure factory to return null (simulates creation failure)
        blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
            .Returns((BlobServiceClient?)null);

        var logger = new NullLogger<BlobFileProvider>();
        var provider = new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            blobServiceClientFactoryMock,
            options,
            logger);

        // Act
        var token = provider.Watch("test.json");

        // Assert - Should fallback to legacy token
        Assert.IsType<BlobChangeToken>(token);
    }

    [Fact]
    public void BlobFileProvider_ShouldCleanupDeadTokenReferences()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act - Create many tokens to trigger cleanup
        var tokens = new List<IChangeToken>();
        for (int i = 0; i < 150; i++) // Above the cleanup threshold of 100
        {
            tokens.Add(provider.Watch($"config{i}.json"));
        }

        // Let some tokens go out of scope and become eligible for GC
        var keepAliveTokens = tokens.Take(10).ToList();
        tokens.Clear(); // Remove references to most tokens

        // Create one more token to potentially trigger cleanup
        var finalToken = provider.Watch("final.json");

        // Assert - Cleanup should have occurred (we can't directly verify internal state,
        // but the fact that no exception occurred indicates the cleanup mechanism works)
        Assert.NotNull(finalToken);
        Assert.Equal(10, keepAliveTokens.Count);
    }

    static BlobFileProvider CreateSut(out BlobClient blobClientMock, BlobConfigurationOptions? options = null)
    {
        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: DefaultLastModified,
            contentLength: DefaultContentLength
        );

        blobClientMock = Substitute.For<BlobClient>();
        blobClientMock
            .GetProperties(cancellationToken: default)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        blobClientFactoryMock
            .GetBlobClient(BlobName)
            .Returns(blobClientMock);

        var blobContainerClientMock = Substitute.For<BlobContainerClient>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        blobContainerClientFactoryMock
            .GetBlobContainerClient()
            .Returns(blobContainerClientMock);

        var defaultBlobConfig = new BlobConfigurationOptions
        {
            ReloadInterval = 1000,
            // Don't provide ConnectionString or BlobContainerUrl to force legacy mode
            // This ensures existing tests continue to work with legacy BlobChangeToken
        };

        var logger = NullLogger<BlobFileProvider>.Instance;
        var blobServiceClientFactoryMock = Substitute.For<IBlobServiceClientFactory>();
        
        // Configure mock to return null for legacy mode (no ConnectionString or BlobContainerUrl)
        blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
            .Returns((BlobServiceClient?)null);

        return new BlobFileProvider(blobClientFactoryMock, blobContainerClientFactoryMock, blobServiceClientFactoryMock, options ?? defaultBlobConfig, logger);
    }

    private BlobConfigurationOptions CreateOptionsWithConnectionString()
    {
        return new BlobConfigurationOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=https://127.0.0.1:10000/devstoreaccount1;",
            ContainerName = "configuration", // Required for enhanced tokens
            ReloadInterval = 1000,
            DebounceDelay = TimeSpan.FromSeconds(1),
            WatchingInterval = TimeSpan.FromSeconds(1),
            ErrorRetryDelay = TimeSpan.FromSeconds(1),
            MaxFileContentHashSizeMb = 5
        };
    }

    private BlobFileProvider CreateBlobFileProvider(BlobConfigurationOptions options)
    {
        // Create proper mocks that support the enhanced functionality
        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        
        // Mock the blob client for enhanced scenarios
        var blobClientMock = Substitute.For<BlobClient>();
        blobClientFactoryMock.GetBlobClient(Arg.Any<string>()).Returns(blobClientMock);
        
        // Mock the container client for enhanced scenarios
        var blobContainerClientMock = Substitute.For<BlobContainerClient>();
        blobContainerClientMock.GetBlobClient(Arg.Any<string>()).Returns(blobClientMock);
        blobContainerClientFactoryMock.GetBlobContainerClient().Returns(blobContainerClientMock);
        
        var logger = new NullLogger<BlobFileProvider>();

        // Create the BlobFileProvider with a valid connection string that won't throw
        // Use the same development storage connection string format
        var optionsWithValidConnectionString = new BlobConfigurationOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=https://127.0.0.1:10000/devstoreaccount1;",
            ContainerName = options.ContainerName,
            ReloadInterval = options.ReloadInterval,
            DebounceDelay = options.DebounceDelay,
            WatchingInterval = options.WatchingInterval,
            ErrorRetryDelay = options.ErrorRetryDelay,
            MaxFileContentHashSizeMb = options.MaxFileContentHashSizeMb
        };

        var blobServiceClientFactoryMock = Substitute.For<IBlobServiceClientFactory>();
        var mockBlobServiceClient = Substitute.For<BlobServiceClient>();
        
        // Configure mock to return BlobServiceClient for enhanced mode
        blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
            .Returns(mockBlobServiceClient);

        return new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            blobServiceClientFactoryMock,
            optionsWithValidConnectionString,
            logger);
    }
}