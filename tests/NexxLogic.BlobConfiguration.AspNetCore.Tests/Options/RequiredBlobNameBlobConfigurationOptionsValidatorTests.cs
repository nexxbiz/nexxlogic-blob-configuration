using FluentValidation.TestHelper;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Options;

public class RequiredBlobNameBlobConfigurationOptionsValidatorTests
{
    private static RequiredBlobNameBlobConfigurationOptionsValidator GetSut() => new();

    [Fact]
    public void Validate_ShouldHaveError_WhenAnyBlobReferenceIsEmpty()
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
        var sut = GetSut();

        // Act
        var result = sut.TestValidate(blobConfig);

        // Assert
        result.ShouldHaveValidationErrorFor(b => b.BlobName);
    }
}
