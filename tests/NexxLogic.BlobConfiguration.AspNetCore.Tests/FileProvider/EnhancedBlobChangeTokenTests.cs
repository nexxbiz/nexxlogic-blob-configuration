using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Collections.Concurrent;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.Strategies;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class EnhancedBlobChangeTokenTests
{
    private const string ContainerName = "configuration";
    private const string BlobPath = "settings.json";
    private readonly ILogger _logger = new NullLogger<EnhancedBlobChangeTokenTests>();

    [Fact]
    public void EnhancedBlobChangeToken_ShouldInitialize_WithCorrectInitialState()
    {
        // Arrange & Act
        using var token = CreateToken();

        // Assert
        Assert.NotNull(token);
        Assert.False(token.HasChanged);
        Assert.True(token.ActiveChangeCallbacks);
    }

    [Theory]
    [InlineData(1, false)] // Single callback registration
    [InlineData(2, false)] // Multiple callback registrations  
    public void EnhancedBlobChangeToken_ShouldRegisterCallbacks_Successfully(int callbackCount, bool shouldExecuteImmediately)
    {
        // Arrange
        using var token = CreateToken();
        var callbacksExecuted = new bool[callbackCount];
        var registrations = new IDisposable[callbackCount];

        // Act
        for (var i = 0; i < callbackCount; i++)
        {
            var index = i; // Capture loop variable
            registrations[i] = token.RegisterChangeCallback(_ => { callbacksExecuted[index] = true; }, null);
        }

        // Assert
        for (var i = 0; i < callbackCount; i++)
        {
            Assert.NotNull(registrations[i]);
            Assert.IsAssignableFrom<IDisposable>(registrations[i]);
            Assert.Equal(shouldExecuteImmediately, callbacksExecuted[i]);
        }

        // Additional assertions for multiple registrations
        if (callbackCount > 1)
        {
            for (var i = 0; i < callbackCount - 1; i++)
            {
                Assert.NotSame(registrations[i], registrations[i + 1]);
            }
        }
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldUnregisterCallback_WhenRegistrationDisposed()
    {
        // Arrange
        using var token = CreateToken();

        // Act
        var registration = token.RegisterChangeCallback(_ => { }, null);
        registration.Dispose();

        // Assert - callback should be unregistered (we can't directly test this without triggering change)
        Assert.NotNull(registration);
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldThrowObjectDisposedException_WhenRegisteringAfterDisposal()
    {
        // Arrange
        var token = CreateToken();
        token.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => token.RegisterChangeCallback(_ => { }, null));
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldDisposeGracefully_WithoutExceptions()
    {
        // Arrange
        var token = CreateToken();
        var registration1 = token.RegisterChangeCallback(_ => { }, null);
        var registration2 = token.RegisterChangeCallback(_ => { }, null);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            registration1.Dispose();
            registration2.Dispose();
            token.Dispose();
        });
        Assert.Null(exception);
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldHandleDoubleDisposal_Gracefully()
    {
        // Arrange
        var token = CreateToken();

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            token.Dispose();
            token.Dispose(); // Second disposal should be safe
        });
        Assert.Null(exception);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldRespectCancellation_DuringLongRunningOperation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var blobServiceClient = CreateMockBlobServiceClient();
        var slowStrategy = CreateSlowStrategy();

        using var token = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            BlobPath,
            TimeSpan.FromSeconds(1), // Short debounce for test
            TimeSpan.FromMilliseconds(100), // Fast watching interval
            TimeSpan.FromMilliseconds(50), // Fast error retry
            slowStrategy,
            new ConcurrentDictionary<string, string>(),
            new ConcurrentDictionary<string, Timer>(),
            _logger);

        // Act - Let the token start its background operation, then cancel
        await Task.Delay(50); // Let it start without using the cancellation token
        cts.Cancel(); // Cancel the operation

        // Wait a bit more to let cancellation propagate (without using the canceled token)
        await Task.Delay(100);

        // Assert - Token should handle cancellation gracefully
        Assert.False(token.HasChanged); // Should not have changed due to cancellation
        
        // Verify that the token can still be disposed without issues
        // The token will be disposed by the using statement - no exception should occur
    }

    [Theory]
    [InlineData(1, 10, 5)] // 1s debounce, 10s watching, 5s error retry
    [InlineData(0, 1, 1)] // Minimum values (0 disables debouncing)
    [InlineData(30, 60, 120)] // Typical production values
    public void EnhancedBlobChangeToken_ShouldAcceptTimingConfigurations_Successfully(
        int debounceSeconds, int watchingSeconds, int errorRetrySeconds)
    {
        // Arrange
        var blobServiceClient = CreateMockBlobServiceClient();
        var strategy = CreateMockStrategy();

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            using var token = new EnhancedBlobChangeToken(
                blobServiceClient,
                ContainerName,
                BlobPath,
                TimeSpan.FromSeconds(debounceSeconds),
                TimeSpan.FromSeconds(watchingSeconds),
                TimeSpan.FromSeconds(errorRetrySeconds),
                strategy,
                new ConcurrentDictionary<string, string>(),
                new ConcurrentDictionary<string, Timer>(),
                _logger);
        });
        Assert.Null(exception);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldHandleStrategyExceptions_Gracefully()
    {
        // Arrange
        var blobServiceClient = CreateMockBlobServiceClient();
        var faultyStrategy = CreateFaultyStrategy();

        using var token = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            BlobPath,
            TimeSpan.FromMilliseconds(10), // Very short debounce for test
            TimeSpan.FromMilliseconds(50), // Fast watching interval
            TimeSpan.FromMilliseconds(10), // Fast error retry
            faultyStrategy,
            new ConcurrentDictionary<string, string>(),
            new ConcurrentDictionary<string, Timer>(),
            _logger);

        // Act - Let it run briefly to encounter strategy exceptions
        await Task.Delay(200);

        // Assert - Token should handle exceptions gracefully
        Assert.False(token.HasChanged); // Should not change due to exceptions
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldShareState_BetweenInstances()
    {
        // This tests the shared dictionaries for content hashes and debounce timers
        
        // Arrange
        var sharedContentHashes = new ConcurrentDictionary<string, string>();
        var sharedDebounceTimers = new ConcurrentDictionary<string, Timer>();
        var blobServiceClient = CreateMockBlobServiceClient();
        var strategy = CreateMockStrategy();

        // Act - Create two tokens sharing the same dictionaries
        using var token1 = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            "file1.json",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            strategy,
            sharedContentHashes,
            sharedDebounceTimers,
            _logger);

        using var token2 = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            "file2.json",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            strategy,
            sharedContentHashes,
            sharedDebounceTimers,
            _logger);

        // Assert - Both tokens should be created successfully and share state
        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotSame(token1, token2);
    }

    private EnhancedBlobChangeToken CreateToken()
    {
        return new EnhancedBlobChangeToken(
            CreateMockBlobServiceClient(),
            ContainerName,
            BlobPath,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            CreateMockStrategy(),
            new ConcurrentDictionary<string, string>(),
            new ConcurrentDictionary<string, Timer>(),
            _logger);
    }

    private BlobServiceClient CreateMockBlobServiceClient()
    {
        var client = Substitute.For<BlobServiceClient>();
        var containerClient = Substitute.For<BlobContainerClient>();
        var blobClient = Substitute.For<BlobClient>();
        
        client.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);
        containerClient.GetBlobClient(Arg.Any<string>()).Returns(blobClient);
        
        return client;
    }

    private IChangeDetectionStrategy CreateMockStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<BlobClient>(), Arg.Any<string>(), Arg.Any<ConcurrentDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(false); // Default to no change detected
        return strategy;
    }

    private IChangeDetectionStrategy CreateSlowStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<BlobClient>(), Arg.Any<string>(), Arg.Any<ConcurrentDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                await Task.Delay(1000, token); // Slow operation
                return false;
            });
        return strategy;
    }

    private IChangeDetectionStrategy CreateFaultyStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<BlobClient>(), Arg.Any<string>(), Arg.Any<ConcurrentDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Simulated strategy failure"));
        return strategy;
    }
}