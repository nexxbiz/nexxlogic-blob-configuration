using FluentValidation.TestHelper;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class BlobConfigurationOptionsValidatorTests
{
    private static BlobConfigurationOptionsValidator GetSut() => new();

    [Fact]
    public void Validate_ShouldHaveError_WhenAnyBlobReferenceIsEmpty()
    {
        // Arrange
        var blobConfig = CreateValidOptions(reloadOnChange: true, reloadInterval: 1);
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.ReloadInterval);
    }

    [Fact]
    public void Validate_ShouldNotHaveError_When_BlobNameRequired_And_BlobNameEmpty()
    {
        // Arrange
        var blobConfig = CreateValidOptions(blobName: "", reloadOnChange: true, reloadInterval: 1);
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldNotHaveValidationErrorFor(b => b.BlobName);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Validate_ReloadInterval_ShouldValidateBasedOnReloadOnChange(bool reloadOnChange, bool shouldHaveError)
    {
        // Arrange
        var blobConfig = CreateValidOptions(reloadOnChange: reloadOnChange, reloadInterval: 1);

        // Act
        var result = GetSut().TestValidate(blobConfig);

        // Assert
        if (shouldHaveError)
        {
            result.ShouldHaveValidationErrorFor(b => b.ReloadInterval);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(b => b.ReloadInterval);
        }
    }

    [Theory]
    [InlineData("connection", "containerurl", null, "Cannot specify container url together with connection string or BlobServiceClientFactory. Please choose one.")]
    [InlineData("", "", null, "Neither connection string, container url, nor BlobServiceClientFactory is specified. Please configure one.")]
    [InlineData(null, "containerurl", true, "Cannot specify container url together with connection string or BlobServiceClientFactory. Please choose one.")]
    public void Validate_ConnectionConfiguration_ShouldHaveError_WhenInvalidCombination(
        string? connectionString,
        string? containerUrl,
        bool? hasFactory,
        string expectedErrorMessage)
    {
        // Arrange
        var blobConfig = CreateOptionsWithConnectionConfig(
            connectionString,
            containerUrl,
            hasFactory == true ? CreateMockFactory() : null);

        // Act
        var result = GetSut().TestValidate(blobConfig);

        // Assert
        AssertConnectionConfigurationError(result, expectedErrorMessage);
    }

    [Fact]
    public void Validate_ShouldNotHaveError_When_BlobServiceClientFactory_IsSpecified_And_ConnectionString_And_ContainerUrl_Are_Empty()
    {
        // Arrange
        var blobConfig = CreateOptionsWithConnectionConfig("", "", CreateMockFactory());

        // Act
        var result = GetSut().TestValidate(blobConfig);

        // Assert
        result.ShouldNotHaveValidationErrorFor("ConnectionString_BlobContainerUrl");
    }

    private static BlobConfigurationOptions CreateValidOptions(
        string connectionString = "CONNECTION_STRING",
        string containerName = "CONTAINER_NAME",
        string blobName = "BLOB_NAME",
        bool? reloadOnChange = null,
        int? reloadInterval = null)
    {
        return new()
        {
            ConnectionString = connectionString,
            ContainerName = containerName,
            BlobName = blobName,
            ReloadOnChange = reloadOnChange ?? false,
            ReloadInterval = reloadInterval ?? 0
        };
    }

    private static BlobConfigurationOptions CreateOptionsWithConnectionConfig(
        string? connectionString,
        string? containerUrl,
        Func<Azure.Storage.Blobs.BlobServiceClient>? factory)
    {
        return new()
        {
            ConnectionString = connectionString ?? string.Empty,
            BlobContainerUrl = containerUrl ?? string.Empty,
            BlobServiceClientFactory = factory,
            ContainerName = "container",
            BlobName = "blob"
        };
    }

    private static Func<Azure.Storage.Blobs.BlobServiceClient> CreateMockFactory() =>
        () => throw new NotSupportedException("Factory should not be invoked during validation.");

    private static void AssertConnectionConfigurationError(
        TestValidationResult<BlobConfigurationOptions> result,
        string expectedErrorMessage)
    {
        result.ShouldHaveValidationErrorFor("ConnectionString_BlobContainerUrl");
        result.Errors
            .Any(x => x.ErrorMessage == expectedErrorMessage)
            .Should()
            .BeTrue();
    }
}
