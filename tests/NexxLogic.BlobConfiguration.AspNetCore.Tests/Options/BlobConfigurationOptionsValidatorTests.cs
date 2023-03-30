using FluentValidation.TestHelper;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class BlobConfigurationOptionsValidatorTests
{
    private static BlobConfigurationOptionsValidator GetSut(bool blobNameIsRequired = true) => new(blobNameIsRequired);

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
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.ReloadInterval);
    }

    [Fact]
    public void Validate_ShouldHaveError_When_BlobNameRequired_And_BlobNameEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "",
            ReloadOnChange = true,
            ReloadInterval = 1
        };
        var sut = GetSut(blobNameIsRequired: true);

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.BlobName);
    }

    [Fact]
    public void Validate_ShouldNotHaveError_When_BlobNameRequired_And_BlobNameEmpty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "CONNECTION_STRING",
            ContainerName = "CONTAINER_NAME",
            BlobName = "",
            ReloadOnChange = true,
            ReloadInterval = 1
        };
        var sut = GetSut(blobNameIsRequired: false);

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldNotHaveValidationErrorFor(b => b.BlobName);
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenReloadIntervalIsTooLow_And_ReloadOnChange_EqualsTrue()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "conectionstring",
            ContainerName = "container",
            BlobName = "asd",
            ReloadInterval = 1,
            ReloadOnChange= true
        };
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.ReloadInterval);
    }

    [Fact]
    public void Validate_ShouldNotHaveError_WhenReloadIntervalIsTooLow_And_ReloadOnChange_EqualsFalse()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "conectionstring",
            ContainerName = "container",
            BlobName = "asd",
            ReloadInterval = 1,
            ReloadOnChange = false
        };
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldNotHaveValidationErrorFor(b => b.ReloadInterval);
    }


    [Fact]
    public void Validate_ShouldHaveError_When_BothConnectionString_And_ContainerUrl_AreSpecified()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "something",
            BlobContainerUrl = "containerurl",
            ContainerName = "container",
            BlobName = "blob"
        };
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result
            .ShouldHaveValidationErrorFor("ConnectionString_BlobContainerUrl");

        result.Errors
            .Any(x => x.ErrorMessage == "Cannot specify both container url and connection string. Please choose one.")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Validate_ShouldHaveError_When_Both_ConnectionString_And_ContainerUrl_Are_Empty()
    {
        // Arrange
        var blobConfig = new BlobConfigurationOptions
        {
            ConnectionString = "",
            BlobContainerUrl = "",
            ContainerName = "container",
            BlobName = "blob"
        };
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result
            .ShouldHaveValidationErrorFor("ConnectionString_BlobContainerUrl");

        result.Errors
            .Any(x => x.ErrorMessage == "Neither connection string nor container url is specified. Please choose one.")
            .Should()
            .BeTrue();
    }
}
