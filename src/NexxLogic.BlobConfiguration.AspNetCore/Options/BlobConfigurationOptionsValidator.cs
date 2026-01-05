using FluentValidation;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal class BlobConfigurationOptionsValidator : AbstractValidator<BlobConfigurationOptions>
{
    public BlobConfigurationOptionsValidator()
    {
        RuleFor(options => options.ReloadInterval)
            .GreaterThanOrEqualTo(5000)
            .When(options => options.ReloadOnChange);

        RuleFor(options => options)
           .Must((options, _, context) =>
           {
               if (!string.IsNullOrWhiteSpace(options.BlobContainerUrl) &&
                   (!string.IsNullOrWhiteSpace(options.ConnectionString) || options.BlobServiceClientFactory != null))
               {
                   context.AddFailure(
                       "ConnectionString_BlobContainerUrl",
                       "Cannot specify container url together with connection string or BlobServiceClientFactory. Please choose one.");
                   return false;
               }
               return true;
           });

        RuleFor(options => options)
           .Must((options, _, context) =>
           {
               if (string.IsNullOrWhiteSpace(options.ConnectionString) &&
                   string.IsNullOrWhiteSpace(options.BlobContainerUrl) &&
                   options.BlobServiceClientFactory == null)
               {
                   context.AddFailure(
                       "ConnectionString_BlobContainerUrl",
                       "Neither connection string, container url, nor BlobServiceClientFactory is specified. Please configure one.");
                   return false;
               }
               return true;
           });


        RuleFor(options => options.ContainerName).NotEmpty();
    }
}