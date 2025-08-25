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
}, logger);
builder.Configuration.AddJsonBlob(config =>
{
    builder.Configuration.GetSection("BlobConfiguration").Bind(config);
    config.BlobName = "Folder/settings.json";
}, logger);
builder.Services.Configure<ExampleOptions>(builder.Configuration.GetSection("ExampleSettings"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/config", (IOptionsSnapshot<ExampleOptions> exampleOptions) => exampleOptions)
   .WithName("GetConfiguration");

app.Run();