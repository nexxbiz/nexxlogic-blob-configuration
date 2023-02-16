using FluentValidation;
using Microsoft.Extensions.Configuration;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;

namespace NexxLogic.BlobConfiguration.AspNetCore.Extensions;

public static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddJsonBlob(this IConfigurationBuilder builder, Action<BlobConfigurationOptions> configure)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        new BlobConfigurationOptionsValidator().ValidateAndThrow(options);

        return builder.AddJsonFile(source =>
        {
            source.FileProvider = new BlobFileProvider(new BlobClientFactory(options), 
                new BlobContainerClientFactory(options), options);
            source.Optional = options.Optional;
            source.ReloadOnChange = options.ReloadOnChange;
            source.Path = options.BlobName;
        });
    }

    public static IConfigurationBuilder AddAllJsonBlobsInContainer(this IConfigurationBuilder builder, 
        Action<BlobConfigurationOptions> configure)
    {
        var options = new BlobConfigurationOptions();
        configure?.Invoke(options);
        var blobClientfactory = new BlobClientFactory(options);
        var blobContainerClientfactory = new BlobContainerClientFactory(options);
        var provider = new BlobFileProvider(blobClientfactory, blobContainerClientfactory, options);

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
                    blobContainerClientfactory, blobOptionsConfiguration);
                source.Optional = blobOptionsConfiguration.Optional;
                source.ReloadOnChange = blobOptionsConfiguration.ReloadOnChange;
                source.Path = blobOptionsConfiguration.BlobName;
            });
        }
        return builder;
    }
}
