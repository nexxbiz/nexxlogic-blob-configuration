using System.ComponentModel.DataAnnotations;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class RequiredBlobNameBlobConfigurationOptionsValidatorTests
{
    private static ValidationContext CreateValidationContext(BlobConfigurationOptions options) 
        => new(options);

    [Fact]
    public void Validate_ShouldHaveError_WhenBlobNameIsEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "", // Empty blob name should fail validation
            ReloadOnChange = false,
            ReloadInterval = 10000
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = RequiredBlobNameBlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result?.ErrorMessage);
        Assert.Contains("BlobName is required", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenBlobNameIsProvided()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "settings.json", // Valid blob name
            ReloadOnChange = false,
            ReloadInterval = 10000
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = RequiredBlobNameBlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void ValidateAndThrow_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "", // Empty blob name
            ReloadOnChange = false,
            ReloadInterval = 10000
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => RequiredBlobNameBlobConfigurationOptionsValidator.ValidateAndThrow(blobConfig));
        Assert.Contains("Invalid BlobConfiguration values: BlobName is required", exception.Message);
    }

    [Fact]
    public void ValidateAndThrow_ShouldNotThrow_WhenBlobNameIsProvided()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "settings.json", // Valid blob name
            ReloadOnChange = false,
            ReloadInterval = 10000
        };

        // Act & Assert
        var exception = Record.Exception(() => RequiredBlobNameBlobConfigurationOptionsValidator.ValidateAndThrow(blobConfig));
        Assert.Null(exception);
    }
}