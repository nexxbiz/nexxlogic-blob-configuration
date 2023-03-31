using FluentValidation;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal class RequiredBlobNameBlobConfigurationOptionsValidator : BlobConfigurationOptionsValidator
{
    public RequiredBlobNameBlobConfigurationOptionsValidator() : base()
    {
        RuleFor(options => options.BlobName).NotEmpty();
    }
}