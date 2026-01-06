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
        configure(options);
        RequiredBlobNameBlobConfigurationOptionsValidator.ValidateAndThrow(options);
        var blobContainerClientFactory = new BlobContainerClientFactory(options);
        var blobClientfactory = new BlobClientFactory(blobContainerClientFactory);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(
                blobClientfactory,
                blobContainerClientFactory, 
                options, 
                logger
            );
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
        configure(options);
        BlobConfigurationOptionsValidator.ValidateAndThrow(options);

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
                    BlobContainerUrl = options.BlobContainerUrl,
                    ContainerName = options.ContainerName,
                    Optional = options.Optional,
                    ReloadInterval = options.ReloadInterval,
                    ReloadOnChange = options.ReloadOnChange,
                    // Enhanced properties
                    DebounceDelaySeconds = options.DebounceDelaySeconds,

                    MaxFileContentHashSizeMb = options.MaxFileContentHashSizeMb,
                    WatchingIntervalSeconds = options.WatchingIntervalSeconds,
                    ErrorRetryDelaySeconds = options.ErrorRetryDelaySeconds
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