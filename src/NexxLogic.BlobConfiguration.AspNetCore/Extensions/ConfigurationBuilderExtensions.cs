using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var blobClientFactory = new BlobClientFactory(blobContainerClientFactory);
        var blobServiceClientLogger = new NullLogger<BlobServiceClientFactory>();
        var blobServiceClientFactory = new BlobServiceClientFactory(blobServiceClientLogger);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(
                blobClientFactory,
                blobContainerClientFactory,
                blobServiceClientFactory,
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

        var blobContainerClientFactory = new BlobContainerClientFactory(options);
        var blobClientFactory = new BlobClientFactory(blobContainerClientFactory);
        var blobServiceClientLogger = new NullLogger<BlobServiceClientFactory>();
        var blobServiceClientFactory = new BlobServiceClientFactory(blobServiceClientLogger);

        var provider = new BlobFileProvider(blobClientFactory, blobContainerClientFactory, blobServiceClientFactory, options, logger);

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
                    DebounceDelay = options.DebounceDelay,
                    MaxFileContentHashSizeMb = options.MaxFileContentHashSizeMb,
                    WatchingInterval = options.WatchingInterval,
                    ErrorRetryDelay = options.ErrorRetryDelay
                };

                source.FileProvider = new BlobFileProvider(
                    blobClientFactory,
                    blobContainerClientFactory,
                    blobServiceClientFactory,
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