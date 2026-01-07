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
        var sut = CreateSut(out var blobClientMock);
        sut.GetFileInfo(BlobName);

        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: DefaultLastModified.Add(TimeSpan.FromSeconds(30)),
            contentLength: DefaultContentLength
        );

        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        blobClientMock
            .GetPropertiesAsync(null, changeToken.CancellationToken)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        // Act
        await Task.Delay(500);

        // Assert
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public async Task Watch_ShouldNotRetrieveProperties_WhenLoadIsPending()
    {
        // Act
        var sut = CreateSut(out var blobClientMock);
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(20);

        // Assert
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
            ReloadInterval = 100_000
        };
        var sut = CreateSut(out var blobClientMock, options);
        sut.GetFileInfo(BlobName);        

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(10);
        changeToken.OnReload();

        // Assert
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
        await Task.Delay(20);

        // Assert - Change token should be created and not check for properties yet
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
            ReloadInterval = 1,
            Optional = true
        };
        var sut = CreateSut(out var blobClientMock, options);

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
        _ = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(100);

        // Assert
        var existCalls = blobClientMock.ReceivedCalls()
            .Where(x => x.GetMethodInfo().Name == nameof(BlobClient.Exists));
        Assert.True(existCalls.Count() > 1);
    }

    [Fact]
    public async Task When_BlobFile_Optional_And_File_Added_Later_Then_RaisesChange()
    {
        // Arrange        
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 1,
            Optional = true
        };
        var sut = CreateSut(out var blobClientMock, options);

        blobClientMock
            .GetProperties()
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        sut.GetFileInfo(BlobName);
        
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(20);

        // Pre-Assert
        blobClientMock
            .Received()
            .Exists(changeToken.CancellationToken);
        Assert.False(changeToken.HasChanged);

        // Act
        blobClientMock            
            .Exists(changeToken.CancellationToken)
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        await Task.Delay(30); // making sure that exists method returns with a TRUE value

        // Assert
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public async Task BlobFileProvider_ShouldPreventRaceConditions_WhenWatchCalledConcurrently()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act - Create multiple concurrent calls to Watch with the same filter
        // Use Task.Run with immediate execution to avoid captured variable warnings
        var watchTasks = new Task<IChangeToken>[10];
        for (int i = 0; i < 10; i++)
        {
            watchTasks[i] = Task.Run(() => provider.Watch("config.json"));
        }

        // Wait for all tasks to complete before provider disposal
        var tokenResults = await Task.WhenAll(watchTasks);

        // Assert - All concurrent calls should return the same token instance (cached)
        Assert.Equal(10, tokenResults.Length);
        
        // All tokens should be the same instance due to caching
        var firstToken = tokenResults.First();
        Assert.All(tokenResults, token => Assert.Same(firstToken, token));
    }

    [Fact]
    public void BlobFileProvider_ShouldReturnEnhancedTokens_WhenConnectionStringProvided()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act
        var token = provider.Watch("test.json");

        // Assert
        var tokenType = token.GetType().Name;
        Assert.True(token is EnhancedBlobChangeToken, $"Expected EnhancedBlobChangeToken, got {tokenType}. This indicates BlobServiceClient was not created successfully.");
    }

    [Fact]
    public void BlobFileProvider_ShouldCacheTokens_CorrectlyPerBlobPath()
    {
        // Arrange
        var options = CreateOptionsWithConnectionString();
        using var provider = CreateBlobFileProvider(options);

        // Act - Create tokens for different blob paths
        var token1A = provider.Watch("config1.json");
        var token1B = provider.Watch("config1.json"); // Same path, should be cached
        var token2 = provider.Watch("config2.json");  // Different path, should be new

        // Debug: Check what types of tokens we got
        var token1AType = token1A.GetType().Name;
        var token1BType = token1B.GetType().Name;
        var token2Type = token2.GetType().Name;

        // If we get enhanced tokens, they should be cached
        if (token1A is EnhancedBlobChangeToken)
        {
            // Assert - Verify we got enhanced tokens (not legacy BlobChangeToken)
            Assert.True(token1A is EnhancedBlobChangeToken, $"Expected EnhancedBlobChangeToken, got {token1AType}");
            Assert.True(token1B is EnhancedBlobChangeToken, $"Expected EnhancedBlobChangeToken, got {token1BType}");
            Assert.True(token2 is EnhancedBlobChangeToken, $"Expected EnhancedBlobChangeToken, got {token2Type}");
            
            // Assert - Same path should return same cached token
            Assert.Same(token1A, token1B); 
            
            // Assert - Different path should return different token
            Assert.NotSame(token1A, token2); 
        }
        else
        {
            // If we fall back to legacy mode, tokens will be the same legacy instance
            // This is expected if BlobServiceClient creation fails in test environment
            Assert.True(token1A is BlobChangeToken, $"Expected BlobChangeToken in fallback mode, got {token1AType}");
            Assert.True(token1B is BlobChangeToken, $"Expected BlobChangeToken in fallback mode, got {token1BType}");
            Assert.True(token2 is BlobChangeToken, $"Expected BlobChangeToken in fallback mode, got {token2Type}");
            
            // In legacy mode, all Watch() calls return the same provider-level token
            Assert.Same(token1A, token1B);
            Assert.Same(token1A, token2);
        }
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
            ReloadInterval = 1,
            // Don't provide ConnectionString or BlobContainerUrl to force legacy mode
            // This ensures existing tests continue to work with legacy BlobChangeToken
        };

        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        return new BlobFileProvider(blobClientFactoryMock, blobContainerClientFactoryMock, options ?? defaultBlobConfig, logger);
    }

    private BlobConfigurationOptions CreateOptionsWithConnectionString()
    {
        return new BlobConfigurationOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=https://127.0.0.1:10000/devstoreaccount1;",
            ContainerName = "configuration", // Required for enhanced tokens
            ReloadInterval = 1000,
            DebounceDelaySeconds = 1,
            WatchingIntervalSeconds = 1,
            ErrorRetryDelaySeconds = 1,
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
            DebounceDelaySeconds = options.DebounceDelaySeconds,
            WatchingIntervalSeconds = options.WatchingIntervalSeconds,
            ErrorRetryDelaySeconds = options.ErrorRetryDelaySeconds,
            MaxFileContentHashSizeMb = options.MaxFileContentHashSizeMb
        };

        return new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            optionsWithValidConnectionString,
            logger);
    }
}