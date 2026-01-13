using System.ComponentModel.DataAnnotations;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class BlobConfigurationOptionsValidatorTests
{
    private static ValidationContext CreateValidationContext(BlobConfigurationOptions options) 
        => new(options);

    [Fact]
    public void Validate_ShouldHaveError_WhenReloadIntervalIsTooLowWithReloadOnChange()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "BLOB_NAME",
            ReloadOnChange = true,
            ReloadInterval = 1000 // Less than 5000
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result?.ErrorMessage);
        Assert.Contains("ReloadInterval must be at least 5000 milliseconds", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldSucceed_When_BlobNameIsEmpty_InBaseValidator()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "", // Empty blob name is OK for base validator
            ReloadOnChange = false,
            ReloadInterval = 10000
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenReloadIntervalIsLowAndReloadOnChangeIsFalse()
    {
        // Arrange
        var blobConfig = CreateValidOptions(reloadOnChange: reloadOnChange, reloadInterval: 1);

        // Act
        var result = GetSut().TestValidate(blobConfig);

        // Assert
        if (shouldHaveError)
        {
            ConnectionString = "connectionstring",
            ContainerName = "container",
            BlobName = "asd",
            ReloadInterval = 1000, // Low interval but ReloadOnChange is false
            ReloadOnChange = false
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenBothConnectionStringAndBlobContainerUrlAreProvided()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "connectionstring",
            BlobContainerUrl = "https://test.blob.core.windows.net/container",
            ContainerName = "container",
            BlobName = "blob",
            ReloadInterval = 10000,
            ReloadOnChange = false
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result!.ErrorMessage);
        Assert.Contains("Cannot specify both ConnectionString and BlobContainerUrl", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenNeitherConnectionStringNorBlobContainerUrlAreProvided()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ContainerName = "container",
            BlobName = "blob",
            ReloadInterval = 10000,
            ReloadOnChange = false
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Either ConnectionString or BlobContainerUrl must be specified", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenContainerNameIsEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "connectionstring",
            ContainerName = "", // Empty container name
            BlobName = "blob",
            ReloadInterval = 10000,
            ReloadOnChange = false
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("ContainerName is required", result.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenAllRequiredFieldsAreValid()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "connectionstring",
            ContainerName = "container",
            BlobName = "blob",
            ReloadInterval = 10000,
            ReloadOnChange = false
        };
        var validationContext = CreateValidationContext(blobConfig);

        // Act
        var result = BlobConfigurationOptionsValidator.ValidateOptions(blobConfig, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void ValidateAndThrow_ShouldThrowArgumentException_WhenValidationFails()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            // Missing required fields
            ReloadInterval = 1000,
            ReloadOnChange = true // This will trigger the reload interval validation
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => BlobConfigurationOptionsValidator.ValidateAndThrow(blobConfig));
        Assert.Contains("Invalid BlobConfiguration values:", exception.Message);
    }

    [Fact]
    public void ValidateAndThrow_ShouldNotThrow_WhenValidationSucceeds()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "connectionstring",
            ContainerName = "container",
            BlobName = "blob",
            ReloadInterval = 10000,
            ReloadOnChange = false
        };

        // Act & Assert
        var exception = Record.Exception(() => BlobConfigurationOptionsValidator.ValidateAndThrow(blobConfig));
        Assert.Null(exception);
    }
}