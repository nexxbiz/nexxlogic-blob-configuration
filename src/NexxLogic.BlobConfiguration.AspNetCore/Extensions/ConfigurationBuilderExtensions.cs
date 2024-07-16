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
        new RequiredBlobNameBlobConfigurationOptionsValidator().ValidateAndThrow(options);
        var blobContainerClientfactory = new BlobContainerClientFactory(options);
        var blobClientfactory = new BlobClientFactory(blobContainerClientfactory);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(
                blobClientfactory,
                blobContainerClientfactory, 
                options, 
                logger
            );
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;
            source.Path = options.BlobName;
        });
    }

    /// <summary>
    /// Adds a blob container as a configuration source. All files in the container, that match the prefix when specified, will be watched.
    /// This means that when new JSON blob files are added at runtime, the JSON content will be added as new keys to <see cref="IConfiguration"/>.
    /// When JSON blob files are updated, their respective configuration keys will be updated with their latest contents.
    /// </summary>
    /// <remarks>
    /// ** Please make sure the JSON blob files contain valid JSON objects, and that each root property in every JSON object file has a unique name.
    /// </remarks>
    /// <param name="builder"></param>
    /// <param name="configure"></param>
    /// <param name="logger"></param>
    /// <returns></returns>
    public static IConfigurationBuilder AddJsonBlobContainerFileCollection(
        this IConfigurationBuilder builder,
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobContainerFileCollectionProvider> logger)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator().ValidateAndThrow(options);
        var blobContainerClientfactory = new BlobContainerClientFactory(options);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobContainerFileCollectionProvider(
                blobContainerClientfactory,
                options,
                logger
            );
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;

            if (!string.IsNullOrWhiteSpace(options.Prefix))
            {
                source.Path = options.Prefix;
            }
        });
    }

    public static IConfigurationBuilder AddAllJsonBlobsInContainer(this IConfigurationBuilder builder,
        Action<BlobConfigurationOptions> configure,
        ILogger<BlobFileProvider> logger)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator().ValidateAndThrow(options);

        var blobContainerClientfactory = new BlobContainerClientFactory(options);
        var blobClientfactory = new BlobClientFactory(blobContainerClientfactory);

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

                source.FileProvider = new BlobFileProvider(
                    blobClientfactory,
                    blobContainerClientfactory, 
                    blobOptionsConfiguration,
                    logger
                );
                source.Optional = blobOptionsConfiguration.Optional;
                source.ReloadOnChange = blobOptionsConfiguration.ReloadOnChange;
                source.Path = blobOptionsConfiguration.BlobName;
            });
        }
        return builder;
    }
}
