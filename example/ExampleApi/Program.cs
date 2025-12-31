using ExampleApi;
using Microsoft.Extensions.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<BlobFileProvider>();

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "settings.json";
    config.ReloadOnChange = true;
    config.ReloadInterval = 10_000;
    config.UseContentBasedChangeDetection = true;
    config.DebounceDelaySeconds = 15;
    config.MaxFileContentHashSizeMb = 2;
    config.EnableDetailedLogging = true;
}, logger);

builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "Folder/settings.json";
    
    // Enhanced features for second blob
    config.UseContentBasedChangeDetection = true;
    config.DebounceDelaySeconds = 10;
    config.EnableDetailedLogging = false;
}, logger);
builder.Services.Configure<ExampleOptions>(builder.Configuration.GetSection("ExampleSettings"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/config", (IOptionsSnapshot<ExampleOptions> exampleOptions) => exampleOptions)
   .WithName("GetConfiguration");

app.Run();