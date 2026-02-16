using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NSubstitute;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class BlobConfigurationOptionsValidationTests
{
    [Theory]
    [InlineData(1000, 0, 1, 1, 1)] // Minimum backward-compatible values
    [InlineData(30000, 30, 60, 120, 5)] // Typical production values
    [InlineData(86400000, 3600, 86400, 7200, 1024)] // Maximum values
    [InlineData(1000, 0, 5, 5, 1)] // Zero debounce (disabled)
    public void BlobConfigurationOptions_ShouldPassValidation_WithValidValues(
        int reloadInterval, int debounceDelaySeconds, int watchingIntervalSeconds, int errorRetryDelaySeconds, int maxHashSize)
    {
        // Arrange
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = reloadInterval,
            DebounceDelay = TimeSpan.FromSeconds(debounceDelaySeconds),
            WatchingInterval = TimeSpan.FromSeconds(watchingIntervalSeconds),
            ErrorRetryDelay = TimeSpan.FromSeconds(errorRetryDelaySeconds),
            MaxFileContentHashSizeMb = maxHashSize,
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net",
            ContainerName = "test"
        };

        // Act & Assert
        var exception = Record.Exception(() => CreateBlobFileProvider(options));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1)] // Negative reload interval
    [InlineData(0)] // Zero reload interval
    [InlineData(86400001)] // Exceeds maximum (24 hours + 1 ms)
    public void BlobConfigurationOptions_ShouldFailValidation_WithInvalidReloadInterval(int invalidValue)
    {
        // Arrange
        var options = CreateValidOptions();
        options.ReloadInterval = invalidValue;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("ReloadInterval", exception.Message);
    }

    [Theory]
    [InlineData(-1)] // Negative debounce delay
    [InlineData(3601)] // Exceeds maximum (1 hour + 1 second)
    public void BlobConfigurationOptions_ShouldFailValidation_WithInvalidDebounceDelay(int invalidSeconds)
    {
        // Arrange
        var options = CreateValidOptions();
        options.DebounceDelay = TimeSpan.FromSeconds(invalidSeconds);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("DebounceDelay", exception.Message);
    }

    [Theory]
    [InlineData(0)] // Zero watching interval
    [InlineData(-1)] // Negative watching interval
    [InlineData(86401)] // Exceeds maximum (24 hours + 1 second)
    public void BlobConfigurationOptions_ShouldFailValidation_WithInvalidWatchingInterval(int invalidSeconds)
    {
        // Arrange
        var options = CreateValidOptions();
        options.WatchingInterval = TimeSpan.FromSeconds(invalidSeconds);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("WatchingInterval", exception.Message);
    }

    [Theory]
    [InlineData(0)] // Zero error retry delay
    [InlineData(-1)] // Negative error retry delay
    [InlineData(7201)] // Exceeds maximum (2 hours + 1 second)
    public void BlobConfigurationOptions_ShouldFailValidation_WithInvalidErrorRetryDelay(int invalidSeconds)
    {
        // Arrange
        var options = CreateValidOptions();
        options.ErrorRetryDelay = TimeSpan.FromSeconds(invalidSeconds);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("ErrorRetryDelay", exception.Message);
    }

    [Theory]
    [InlineData(0)] // Zero max hash size
    [InlineData(-1)] // Negative max hash size
    [InlineData(1025)] // Exceeds maximum (1024 MB + 1)
    public void BlobConfigurationOptions_ShouldFailValidation_WithInvalidMaxFileContentHashSizeMb(int invalidValue)
    {
        // Arrange
        var options = CreateValidOptions();
        options.MaxFileContentHashSizeMb = invalidValue;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("MaxFileContentHashSizeMb", exception.Message);
    }

    [Fact]
    public void BlobConfigurationOptions_ShouldFailValidation_WithMultipleInvalidValues()
    {
        // Arrange
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = -1, // Invalid
            DebounceDelay = TimeSpan.FromSeconds(-5), // Invalid
            WatchingInterval = TimeSpan.FromSeconds(0), // Invalid
            ErrorRetryDelay = TimeSpan.FromSeconds(-10), // Invalid
            MaxFileContentHashSizeMb = 0, // Invalid
            ConnectionString = "test",
            ContainerName = "test"
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        
        // Should contain error messages for all invalid properties
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
        Assert.Contains("ReloadInterval", exception.Message);
        Assert.Contains("DebounceDelay", exception.Message);
        Assert.Contains("WatchingInterval", exception.Message);
        Assert.Contains("ErrorRetryDelay", exception.Message);
        Assert.Contains("MaxFileContentHashSizeMb", exception.Message);
    }

    [Fact]
    public void BlobConfigurationOptions_ShouldIncludePropertyName_InErrorMessages()
    {
        // Arrange
        var options = CreateValidOptions();
        options.DebounceDelay = TimeSpan.FromSeconds(5000); // Exceeds maximum of 3600

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        Assert.Contains("DebounceDelay", exception.Message); // Should include the property name in error message
    }

    [Fact]
    public void BlobConfigurationOptions_ShouldWorkWithIntelligentStrategySelection()
    {
        // Arrange
        var options = CreateValidOptions();

        // Act & Assert - Factory intelligently selects strategy, no configuration needed
        var exception = Record.Exception(() => CreateBlobFileProvider(options));
        Assert.Null(exception);
    }

    [Fact]
    public void BlobConfigurationOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange
        var options = new BlobConfigurationOptions();

        // Act & Assert - All defaults should be within valid ranges
        Assert.Equal(30_000, options.ReloadInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DebounceDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.WatchingInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.ErrorRetryDelay);
        Assert.Equal(1, options.MaxFileContentHashSizeMb);
        // Factory is created internally - no configuration property needed

        // All defaults should pass validation (when other required fields are set)
        options.ConnectionString = "test";
        options.ContainerName = "test";
        
        var exception = Record.Exception(() => CreateBlobFileProvider(options));
        Assert.Null(exception);
    }

    [Fact]
    public void BlobConfigurationOptions_ShouldUseDataAnnotationsValidation()
    {
        // This test ensures we're using the DataAnnotations framework correctly
        // by verifying that the error messages match the Range attribute format
        
        // Arrange
        var options = CreateValidOptions();
        options.DebounceDelay = TimeSpan.FromSeconds(-1); // Invalid

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => CreateBlobFileProvider(options));
        
        // Should use the Range attribute error message format
        Assert.Contains("DebounceDelay must be between", exception.Message);
        Assert.Contains("Use 0 to disable debouncing", exception.Message);
    }

    private BlobConfigurationOptions CreateValidOptions()
    {
        return new BlobConfigurationOptions
        {
            ReloadInterval = 30000,
            DebounceDelay = TimeSpan.FromSeconds(30),
            WatchingInterval = TimeSpan.FromSeconds(60),
            ErrorRetryDelay = TimeSpan.FromSeconds(120),
            MaxFileContentHashSizeMb = 5,
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net",
            ContainerName = "test"
        };
    }

    private BlobFileProvider CreateBlobFileProvider(BlobConfigurationOptions options)
    {
        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        var blobServiceClientFactoryMock = Substitute.For<IBlobServiceClientFactory>();
        
        // These tests typically don't provide ConnectionString, so return null for legacy mode
        blobServiceClientFactoryMock.CreateBlobServiceClient(Arg.Any<BlobConfigurationOptions>())
            .Returns((BlobServiceClient?)null);
            
        var logger = new NullLogger<BlobFileProvider>();

        return new BlobFileProvider(
            blobClientFactoryMock,
            blobContainerClientFactoryMock,
            blobServiceClientFactoryMock,
            options,
            logger);
    }
}