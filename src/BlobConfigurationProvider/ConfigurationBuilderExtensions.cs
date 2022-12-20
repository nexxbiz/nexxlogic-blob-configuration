using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace BlobConfigurationProvider;

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
