using FluentValidation;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal class BlobConfigurationOptionsValidator : AbstractValidator<BlobConfigurationOptions>
{
    public BlobConfigurationOptionsValidator(bool blobNameIsRequired)
    {
        RuleFor(options => options.ReloadInterval)
            .GreaterThanOrEqualTo(5000)
            .When(options => options.ReloadOnChange);

        RuleFor(options => options)
           .Must((options, _, context) =>
           {
               if (!string.IsNullOrWhiteSpace(options.ConnectionString) && !string.IsNullOrWhiteSpace(options.BlobContainerUrl))
               {
                   context.AddFailure("ConnectionString_BlobContainerUrl", "Cannot specify both container url and connection string. Please choose one.");
                   return false;
               }
               return true;
           });

        RuleFor(options => options)
           .Must((options, _, context) =>
           {
               if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.BlobContainerUrl))
               {
                   context.AddFailure("ConnectionString_BlobContainerUrl", "Neither connection string nor container url is specified. Please choose one.");
                   return false;
               }
               return true;
           });       


        RuleFor(options => options.ContainerName).NotEmpty();

        if (blobNameIsRequired)
        {
            RuleFor(options => options.BlobName).NotEmpty();
        }
    }
}