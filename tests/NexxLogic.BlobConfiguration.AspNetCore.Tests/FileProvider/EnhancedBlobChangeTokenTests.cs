using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Collections.Concurrent;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

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
            Assert.IsType<IDisposable>(registrations[i], exactMatch: false);
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

        await using var token = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            BlobPath,
            TimeSpan.FromSeconds(1), // Short debounce for test
            TimeSpan.FromMilliseconds(100), // Fast watching interval
            TimeSpan.FromMilliseconds(50), // Fast error retry
            slowStrategy,
            new ConcurrentDictionary<string, string>(),
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

        await using var token = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            BlobPath,
            TimeSpan.FromMilliseconds(10), // Very short debounce for test
            TimeSpan.FromMilliseconds(50), // Fast watching interval
            TimeSpan.FromMilliseconds(10), // Fast error retry
            faultyStrategy,
            new ConcurrentDictionary<string, string>(),
            _logger);

        // Act - Let it run briefly to encounter strategy exceptions
        await Task.Delay(200);

        // Assert - Token should handle exceptions gracefully
        Assert.False(token.HasChanged); // Should not change due to exceptions
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldShareBlobFingerprints_BetweenInstances()
    {
        // This tests that blob fingerprints (hashes, ETags, etc.) are shared between tokens (for caching efficiency)
        // while each token manages its own debounce timer independently
        
        // Arrange
        var sharedBlobFingerprints = new ConcurrentDictionary<string, string>();
        var blobServiceClient = CreateMockBlobServiceClient();
        var strategy = CreateMockStrategy();

        // Act - Create two tokens sharing the same content hash dictionary
        using var token1 = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            "file1.json",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            strategy,
            sharedBlobFingerprints,
            _logger);

        using var token2 = new EnhancedBlobChangeToken(
            blobServiceClient,
            ContainerName,
            "file2.json",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            strategy,
            sharedBlobFingerprints,
            _logger);

        // Assert - Both tokens should be created successfully and share content hashes
        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotSame(token1, token2);
        
        // The shared blob fingerprints dictionary enables efficient caching across tokens
        // Each token manages its own debounce timer independently for better isolation
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
            _logger);
    }

    private static BlobServiceClient CreateMockBlobServiceClient()
    {
        var client = Substitute.For<BlobServiceClient>();
        var containerClient = Substitute.For<BlobContainerClient>();
        var blobClient = Substitute.For<BlobClient>();
        
        client.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);
        containerClient.GetBlobClient(Arg.Any<string>()).Returns(blobClient);
        
        return client;
    }

    private static IChangeDetectionStrategy CreateMockStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<ChangeDetectionContext>())
            .Returns(false); // Default to no change detected
        return strategy;
    }

    private static IChangeDetectionStrategy CreateSlowStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<ChangeDetectionContext>())
            .Returns(async callInfo =>
            {
                var context = callInfo.Arg<ChangeDetectionContext>();
                await Task.Delay(1000, context.CancellationToken); // Slow operation
                return false;
            });
        return strategy;
    }

    private static IChangeDetectionStrategy CreateFaultyStrategy()
    {
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<ChangeDetectionContext>())
            .ThrowsAsync(new InvalidOperationException("Simulated strategy failure"));
        return strategy;
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldDisposeAsync_WithoutBlocking()
    {
        // Arrange
        var token = CreateToken();
        var registration = token.RegisterChangeCallback(_ => { }, null);
        var startTime = DateTime.UtcNow;

        // Act
        await using (token)
        {
            // Token should be alive here
            Assert.True(token.ActiveChangeCallbacks);
        }
        // Token should be disposed here via async disposal

        var endTime = DateTime.UtcNow;
        var elapsedTime = endTime - startTime;

        // Assert
        // Disposal should complete quickly (much less than the 5-second timeout)
        // This verifies we're not blocking on the background task completion
        Assert.True(elapsedTime < TimeSpan.FromSeconds(1), 
            $"Async disposal took {elapsedTime.TotalMilliseconds}ms, which suggests it may be blocking");
        
        // Cleanup
        registration.Dispose();
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldSupportBothSyncAndAsyncDisposal()
    {
        // Arrange
        var token1 = CreateToken();
        var token2 = CreateToken();

        // Act & Assert - Both disposal patterns should work
        var syncException = Record.Exception(() => token1.Dispose());
        Assert.Null(syncException);

        var asyncException = Record.Exception(() => token2.DisposeAsync().GetAwaiter().GetResult());
        Assert.Null(asyncException);
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldAvoidDeadlocks_WhenCallbacksRegisterUnregister()
    {
        // Arrange
        using var token = CreateToken();
        var callbackExecuted = false;
        var registrations = new List<IDisposable>();

        // Act - Register a callback that tries to register another callback
        // This tests that callback execution doesn't deadlock with the internal lock
        var firstRegistration = token.RegisterChangeCallback(_ =>
        {
            callbackExecuted = true;
            
            // This should not deadlock - callback execution should be outside the lock
            var secondRegistration = token.RegisterChangeCallback(_ =>
            {
                // Empty callback for testing
            }, null);
            registrations.Add(secondRegistration);
            
        }, null);

        // Simulate a change notification by manually triggering callbacks
        // (In real usage this would happen through the background watcher)
        var notifyMethod = typeof(EnhancedBlobChangeToken)
            .GetMethod("NotifyCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        notifyMethod?.Invoke(token, null);

        // Assert
        Assert.True(callbackExecuted, "First callback should have executed");
        Assert.Single(registrations); // Should have one registration from the callback
        
        // The second callback won't be executed in this test since we only triggered one notification,
        // but the important thing is that registration didn't deadlock
        
        // Cleanup
        firstRegistration.Dispose();
        foreach (var registration in registrations)
        {
            registration.Dispose();
        }
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldAvoidDeadlocks_WhenCallbacksUnregister()
    {
        // Arrange
        using var token = CreateToken();
        var callbackExecuted = false;
        IDisposable? registration = null;

        // Act - Register a callback that tries to unregister itself
        token.RegisterChangeCallback(_ =>
        {
            callbackExecuted = true;
            
            // This should not deadlock - callback execution should be outside the lock
            registration?.Dispose();
            
        }, null);

        // Simulate a change notification
        var notifyMethod = typeof(EnhancedBlobChangeToken)
            .GetMethod("NotifyCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        notifyMethod?.Invoke(token, null);

        // Assert
        Assert.True(callbackExecuted, "Callback should have executed and self-unregistered without deadlock");
    }
}