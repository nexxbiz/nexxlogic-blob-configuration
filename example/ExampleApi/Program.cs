using ExampleApi;
using Microsoft.Extensions.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var loggerFactory = LoggerFactory.Create(loggerBuilder =>
{
    loggerBuilder.AddConsole();
});

var logger = loggerFactory.CreateLogger<BlobFileProvider>();

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "settings.json";
    config.ReloadOnChange = true;
    
    // Strategy selection is handled automatically
    config.MaxFileContentHashSizeMb = 5;
    config.DebounceDelay = TimeSpan.FromSeconds(15);
    // In enhanced mode, WatchingInterval controls how often the blob is polled for changes
    config.WatchingInterval = TimeSpan.FromSeconds(20); // Poll every 20 seconds
    config.ErrorRetryDelay = TimeSpan.FromSeconds(30); // Retry after 30 seconds on error
}, logger);

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "Folder/settings.json";
    
    // Enhanced features for second blob - using ETag for fast detection
    // Strategy selection is handled automatically
    config.MaxFileContentHashSizeMb = 1; // Use minimum valid value; strategy selection remains automatic
    config.DebounceDelay = TimeSpan.FromSeconds(10);
    config.WatchingInterval = TimeSpan.FromMinutes(1); // Poll every minute for less frequent checks
    config.ErrorRetryDelay = TimeSpan.FromMinutes(2); // 2-minute retry delay for non-critical config
}, logger);
builder.Services.Configure<ExampleOptions>(builder.Configuration.GetSection("ExampleSettings"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/config", (IOptionsSnapshot<ExampleOptions> exampleOptions) => exampleOptions)
   .WithName("GetConfiguration");

app.Run();