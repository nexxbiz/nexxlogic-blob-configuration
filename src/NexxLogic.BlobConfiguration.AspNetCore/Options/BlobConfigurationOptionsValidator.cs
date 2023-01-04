using FluentValidation;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal class BlobConfigurationOptionsValidator : AbstractValidator<BlobConfigurationOptions>
{
    public BlobConfigurationOptionsValidator()
    {
        RuleFor(options => options.ReloadInterval)
            .GreaterThanOrEqualTo(5000)
            .When(options => options.ReloadOnChange);

        RuleFor(options => options.ConnectionString).NotEmpty();
        RuleFor(options => options.ContainerName).NotEmpty();
        RuleFor(options => options.BlobName).NotEmpty();
    }
}