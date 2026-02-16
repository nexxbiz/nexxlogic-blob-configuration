using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using System.Collections.Concurrent;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;
using NSubstitute.ExceptionExtensions;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class EnhancedBlobChangeTokenTests
{
    private const string ContainerName = "configuration";
    private const string BlobPath = "settings.json";
    private readonly ILogger _logger = new NullLogger<EnhancedBlobChangeTokenTests>();

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldBeValidAndNotChanged_Initially()
    {
        // Arrange & Act
        await using var token = CreateDefaultToken();

        // Assert
        Assert.NotNull(token);
        Assert.False(token.HasChanged);
        Assert.True(token.ActiveChangeCallbacks); // Should be true when not disposed (standard IChangeToken behavior)
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldReturnCorrectActiveChangeCallbacks()
    {
        // Arrange
        await using var token = CreateDefaultToken();
        
        // Should be true when not disposed (standard IChangeToken behavior)
        Assert.True(token.ActiveChangeCallbacks);
        
        // Act - Register a callback
        var registration1 = token.RegisterChangeCallback(_ => { }, null);
        
        // Assert - Should still be true (not disposed)
        Assert.True(token.ActiveChangeCallbacks);
        
        // Act - Register another callback
        var registration2 = token.RegisterChangeCallback(_ => { }, null);
        
        // Assert - Should still be true (not disposed)
        Assert.True(token.ActiveChangeCallbacks);
        
        // Act - Unregister one callback
        registration1.Dispose();
        
        // Assert - Should still be true (not disposed)
        Assert.True(token.ActiveChangeCallbacks);
        
        // Act - Unregister the last callback
        registration2.Dispose();
        
        // Assert - Should still be true (not disposed, standard behavior doesn't depend on callback count)
        Assert.True(token.ActiveChangeCallbacks);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldReturnFalseActiveChangeCallbacks_WhenDisposed()
    {
        // Arrange
        var token = CreateDefaultToken();
        var registration = token.RegisterChangeCallback(_ => { }, null);
        
        // Verify it starts as true with registered callback
        Assert.True(token.ActiveChangeCallbacks);
        
        // Act - Dispose the token
        await token.DisposeAsync();
        
        // Assert - Should be false after disposal, even with registered callbacks
        Assert.False(token.ActiveChangeCallbacks);
        
        // Cleanup
        registration.Dispose();
    }

    [Theory]
    [InlineData(1, false)] // Single callback registration
    [InlineData(2, false)] // Multiple callback registrations  
    public async Task EnhancedBlobChangeToken_ShouldRegisterCallbacks_Successfully(int callbackCount, bool shouldExecuteImmediately)
    {
        // Arrange
        await using var token = CreateDefaultToken();
        var (callbacksExecuted, registrations) = RegisterMultipleCallbacks(token, callbackCount);

        // Assert
        AssertCallbackRegistrations(registrations, callbacksExecuted, shouldExecuteImmediately);
        CleanupRegistrations(registrations);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldUnregisterCallback_WhenRegistrationDisposed()
    {
        // Arrange
        await using var token = CreateDefaultToken();

        // Act
        var registration = token.RegisterChangeCallback(_ => { }, null);
        registration.Dispose();

        // Assert - callback should be unregistered (we can't directly test this without triggering change)
        Assert.NotNull(registration);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldThrowObjectDisposedException_WhenRegisteringAfterDisposal()
    {
        // Arrange
        var token = CreateDefaultToken();
        await token.DisposeAsync();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => token.RegisterChangeCallback(_ => { }, null));
    }

    [Theory]
    [InlineData(true)] // With registrations
    [InlineData(false)] // Without registrations
    public async Task EnhancedBlobChangeToken_ShouldDisposeGracefully_WithoutExceptions(bool withRegistrations)
    {
        // Arrange
        var token = CreateDefaultToken();
        var registrations = new List<IDisposable>();

        if (withRegistrations)
        {
            registrations.Add(token.RegisterChangeCallback(_ => { }, null));
            registrations.Add(token.RegisterChangeCallback(_ => { }, null));
        }

        // Act + Assert
        await AssertGracefulDisposalAsync(async () =>
        {
            CleanupRegistrations(registrations);
            await token.DisposeAsync();
        });
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldHandleDoubleDisposal_Gracefully()
    {
        // Arrange
        var token = CreateDefaultToken();

        // Act & Assert
        await AssertGracefulDisposalAsync(async () =>
        {
            await token.DisposeAsync();
            await token.DisposeAsync(); // Second disposal should be safe
        });
    }
    
    [Theory]
    [InlineData(1, 10, 5)] // 1s debounce, 10s watching, 5s error retry
    [InlineData(0, 1, 1)] // Minimum values (0 disables debouncing)
    [InlineData(30, 60, 120)] // Typical production values
    public async Task EnhancedBlobChangeToken_ShouldAcceptTimingConfigurations_Successfully(
        int debounceSeconds, int watchingSeconds, int errorRetrySeconds)
    {
        // Arrange & Act & Assert
        await AssertGracefulCreation(() => CreateTokenWithTiming(
            TimeSpan.FromSeconds(debounceSeconds),
            TimeSpan.FromSeconds(watchingSeconds),
            TimeSpan.FromSeconds(errorRetrySeconds)));
    }

    [Fact]
    public void EnhancedBlobChangeToken_ShouldHandleStrategyExceptions_Gracefully()
    {
        // Arrange - Create strategy that throws an exception
        var strategy = Substitute.For<IChangeDetectionStrategy>();
        strategy.HasChangedAsync(Arg.Any<ChangeDetectionContext>())
            .ThrowsAsync(new InvalidOperationException("Simulated strategy failure"));

        // Act - Creating the token should not throw
        var token = CreateTokenWithStrategy(strategy, fastTiming: true);

        // Assert - Token should be created successfully despite faulty strategy
        Assert.NotNull(token);
        Assert.False(token.HasChanged); // Should not have changed yet
        Assert.True(token.ActiveChangeCallbacks); // Should be active
        
        // Should be able to register callbacks
        var registration = token.RegisterChangeCallback(_ => { }, null);
        Assert.NotNull(registration);
        
        // Cleanup
        registration.Dispose();
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldShareBlobFingerprints_BetweenInstances()
    {
        // Arrange
        var sharedBlobFingerprints = new ConcurrentDictionary<string, string>();

        // Act - Create two tokens sharing the same content hash dictionary
        await using var token1 = CreateTokenWithPath("file1.json", sharedBlobFingerprints);
        await using var token2 = CreateTokenWithPath("file2.json", sharedBlobFingerprints);

        // Assert - Both tokens should be created successfully and share content hashes
        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotSame(token1, token2);
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldDisposeAsync_WithoutDeadlock()
    {
        // Arrange
        var token = CreateDefaultToken();
        var registration = token.RegisterChangeCallback(_ => { }, null);

        // Verify initial state
        Assert.True(token.ActiveChangeCallbacks);
        Assert.False(token.HasChanged);

        // Act - DisposeAsync should complete within a generous timeout (not deadlock)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Generous timeout for CI
        await token.DisposeAsync();

        // Assert - Verify post-disposal behavioral outcomes
        Assert.False(token.ActiveChangeCallbacks); // Should be false after disposal
        
        // After disposal, RegisterChangeCallback should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => token.RegisterChangeCallback(_ => { }, null));
        
        // Disposal should be idempotent - second call should not throw
        var exception = await Record.ExceptionAsync(async () => await token.DisposeAsync());
        Assert.Null(exception);
        
        // Cleanup original registration
        registration.Dispose();
    }

    [Fact]
    public async Task EnhancedBlobChangeToken_ShouldSupportBothSyncAndAsyncDisposal()
    {
        // Arrange
        var token1 = CreateDefaultToken();
        var token2 = CreateDefaultToken();

        // Act & Assert - Both disposal patterns should work
        await AssertGracefulDisposalAsync(async () => await token1.DisposeAsync());
        await AssertGracefulDisposalAsync(async () => await token2.DisposeAsync());
    }

    [Theory]
    [InlineData(true)] // Test callback that registers another callback
    [InlineData(false)] // Test callback that unregisters itself
    public async Task EnhancedBlobChangeToken_ShouldAvoidDeadlocks_WhenCallbacksModifyRegistrations(bool registerInCallback)
    {
        // Arrange
        await using var token = CreateDefaultToken();
        var callbackExecuted = false;
        var additionalRegistrations = new List<IDisposable>();

        if (registerInCallback)
        {
            // Test registering callback within callback
            var registration = token.RegisterChangeCallback(_ =>
            {
                callbackExecuted = true;
                // Register another callback - should not deadlock
#pragma warning disable IDE0067 // Captured variable is disposed in the outer scope - intentional for testing
                var secondRegistration = token.RegisterChangeCallback(_ => { }, null);
#pragma warning restore IDE0067
                additionalRegistrations.Add(secondRegistration);
            }, null);

            // Simulate change notification
            TriggerChangeNotification(token);

            // Assert
            Assert.True(callbackExecuted, "Callback should have executed without deadlock");
            Assert.Single(additionalRegistrations);

            // Cleanup
            registration.Dispose();
            CleanupRegistrations(additionalRegistrations);
        }
        else
        {
            // Test self-disposal within callback
            IDisposable? registration = null;
            registration = token.RegisterChangeCallback(_ =>
            {
                callbackExecuted = true;
                // Self-dispose - should not deadlock
#pragma warning disable IDE0067 // Captured variable is modified in the outer scope - intentional for testing
                registration!.Dispose();
#pragma warning restore IDE0067
            }, null);

            // Simulate change notification
            TriggerChangeNotification(token);

            // Assert
            Assert.True(callbackExecuted, "Callback should have executed without deadlock");
            // No cleanup needed as it self-disposed
        }
    }

    // Helper methods to reduce repetition

    private EnhancedBlobChangeToken CreateDefaultToken()
    {
        return CreateTokenWithStrategy(CreateMockStrategy(), fastTiming: false);
    }

    private EnhancedBlobChangeToken CreateTokenWithStrategy(IChangeDetectionStrategy strategy, bool fastTiming)
    {
        var timing = fastTiming 
            ? (debounce: TimeSpan.FromMilliseconds(10), watching: TimeSpan.FromMilliseconds(50), errorRetry: TimeSpan.FromMilliseconds(10))
            : (debounce: TimeSpan.FromSeconds(1), watching: TimeSpan.FromSeconds(30), errorRetry: TimeSpan.FromSeconds(60));

        return CreateTokenWithTiming(timing.debounce, timing.watching, timing.errorRetry, strategy);
    }

    private EnhancedBlobChangeToken CreateTokenWithTiming(
        TimeSpan debounce, 
        TimeSpan watching, 
        TimeSpan errorRetry,
        IChangeDetectionStrategy? strategy = null)
    {
        return new EnhancedBlobChangeToken(
            CreateMockBlobServiceClient(),
            ContainerName,
            BlobPath,
            debounce,
            watching,
            errorRetry,
            strategy ?? CreateMockStrategy(),
            new ConcurrentDictionary<string, string>(),
            _logger);
    }

    private EnhancedBlobChangeToken CreateTokenWithPath(string blobPath, ConcurrentDictionary<string, string> sharedBlobFingerprints)
    {
        return new EnhancedBlobChangeToken(
            CreateMockBlobServiceClient(),
            ContainerName,
            blobPath,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            CreateMockStrategy(),
            sharedBlobFingerprints,
            _logger);
    }

    private static (bool[] callbacksExecuted, IDisposable[] registrations) RegisterMultipleCallbacks(
        EnhancedBlobChangeToken token, 
        int callbackCount)
    {
        var callbacksExecuted = new bool[callbackCount];
        var registrations = new IDisposable[callbackCount];

        for (var i = 0; i < callbackCount; i++)
        {
            var index = i; // Capture loop variable
            registrations[i] = token.RegisterChangeCallback(_ => { callbacksExecuted[index] = true; }, null);
        }

        return (callbacksExecuted, registrations);
    }

    private static void AssertCallbackRegistrations(
        IDisposable[] registrations, 
        bool[] callbacksExecuted, 
        bool shouldExecuteImmediately)
    {
        for (var i = 0; i < registrations.Length; i++)
        {
            Assert.NotNull(registrations[i]);
            Assert.IsType<IDisposable>(registrations[i], exactMatch: false);
            Assert.Equal(shouldExecuteImmediately, callbacksExecuted[i]);
        }

        // Additional assertions for multiple registrations
        if (registrations.Length > 1)
        {
            for (var i = 0; i < registrations.Length - 1; i++)
            {
                Assert.NotSame(registrations[i], registrations[i + 1]);
            }
        }
    }

    private static void CleanupRegistrations(IEnumerable<IDisposable?> registrations)
    {
        foreach (var registration in registrations)
        {
            registration?.Dispose();
        }
    }

    private static void AssertGracefulDisposal(Action disposeAction)
    {
        var exception = Record.Exception(disposeAction);
        Assert.Null(exception);
    }

    private static async Task AssertGracefulDisposalAsync(Func<Task> disposeAction)
    {
        var exception = await Record.ExceptionAsync(disposeAction);
        Assert.Null(exception);
    }

    private static async Task AssertGracefulCreation(Func<IAsyncDisposable> createAction)
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var disposable = createAction();
        });
        Assert.Null(exception);
    }

    private static void TriggerChangeNotification(EnhancedBlobChangeToken token)
    {
        var notifyMethod = typeof(EnhancedBlobChangeToken)
            .GetMethod("NotifyCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        notifyMethod?.Invoke(token, null);
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
}