using BlobConfigurationProvider.Factories;
using BlobConfigurationProvider.FileProvider;
using BlobConfigurationProvider.Options;
using FluentValidation;

namespace Microsoft.Extensions.Configuration;

public static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddJsonBlob(this IConfigurationBuilder builder, Action<BlobConfigurationOptions> configure)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator().ValidateAndThrow(options);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(new BlobClientFactory(options), options);
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;
            source.Path = options.BlobName;
        });
    }
}
