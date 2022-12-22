using FluentValidation.TestHelper;

namespace BlobConfigurationProvider.Tests.Options;

public class BlobConfigurationOptionsValidatorTests
{
    private readonly BlobConfigurationOptionsValidator _sut;

    public BlobConfigurationOptionsValidatorTests()
    {
        _sut = new();
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenAnyBlobReferenceIsEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "BLOB_NAME",
            ReloadOnChange = true,
            ReloadInterval = 1
        };

        // Act
        var result = _sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.ReloadInterval);
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenReloadIntervalIsTooLow()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = string.Empty,
            ContainerName = string.Empty,
            BlobName = string.Empty
        };

        // Act
        var result = _sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.ConnectionString);
        result.ShouldHaveValidationErrorFor(b => b.ContainerName);
        result.ShouldHaveValidationErrorFor(b => b.BlobName);
    }
}
