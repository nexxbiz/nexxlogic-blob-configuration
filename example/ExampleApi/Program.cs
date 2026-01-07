using ExampleApi;
using Microsoft.Extensions.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
});

var logger = loggerFactory.CreateLogger<BlobFileProvider>();

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "settings.json";
    config.ReloadOnChange = true;
    
    // Strategy selection is handled automatically
    config.MaxFileContentHashSizeMb = 5;
    config.DebounceDelaySeconds = 15;
    // In enhanced mode, WatchingIntervalSeconds controls how often the blob is polled for changes
    config.WatchingIntervalSeconds = 20; // Poll every 20 seconds
    config.ErrorRetryDelaySeconds = 30; // Retry after 30 seconds on error
}, logger);

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "Folder/settings.json";
    
    // Enhanced features for second blob - using ETag for fast detection
    // Strategy selection is handled automatically
    config.MaxFileContentHashSizeMb = 0; // This will cause factory to choose ETag strategy
    config.DebounceDelaySeconds = 10;
    config.WatchingIntervalSeconds = 60; // Poll every 60 seconds for less frequent checks
    config.ErrorRetryDelaySeconds = 120; // Longer retry delay for non-critical config
}, logger);
builder.Services.Configure<ExampleOptions>(builder.Configuration.GetSection("ExampleSettings"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/config", (IOptionsSnapshot<ExampleOptions> exampleOptions) => exampleOptions)
   .WithName("GetConfiguration");

app.Run();