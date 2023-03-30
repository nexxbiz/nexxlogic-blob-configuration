using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Extensions;

public static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddJsonBlob(this IConfigurationBuilder builder, 
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator(blobNameIsRequired: true).ValidateAndThrow(options);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(new BlobClientFactory(options),
                new BlobContainerClientFactory(options), options, logger);
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;
            source.Path = options.BlobName;
        });
    }

    public static IConfigurationBuilder AddAllJsonBlobsInContainer(this IConfigurationBuilder builder,
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator(blobNameIsRequired: false).ValidateAndThrow(options);

        var blobClientfactory = new BlobClientFactory(options);
        var blobContainerClientfactory = new BlobContainerClientFactory(options);

        var provider = new BlobFileProvider(blobClientfactory, blobContainerClientfactory, options, logger);

        foreach (var blobInfo in provider.GetDirectoryContents(""))
        {
            builder.AddJsonFile(source =>
            {
                var blobOptionsConfiguration = new BlobConfigurationOptions
                {
                    BlobName = blobInfo.Name,
                    ConnectionString = options.ConnectionString,
                    ContainerName = options.ContainerName,
                    Optional = options.Optional,
                    ReloadInterval = options.ReloadInterval,
                    ReloadOnChange = options.ReloadOnChange,
                };

                source.FileProvider = new BlobFileProvider(new BlobClientFactory(blobOptionsConfiguration),
                    blobContainerClientfactory, blobOptionsConfiguration,logger);
                source.Optional = blobOptionsConfiguration.Optional;
                source.ReloadOnChange = blobOptionsConfiguration.ReloadOnChange;
                source.Path = blobOptionsConfiguration.BlobName;
            });
        }
        return builder;
    }
}
