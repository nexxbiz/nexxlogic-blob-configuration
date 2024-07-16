using ExampleApi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexxLogic.BlobConfiguration.AspNetCore.Extensions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<BlobFileProvider>();

// Registers and listens to updates of a single blob file
builder.Configuration.AddJsonBlob(config =>
    {
        builder.Configuration.GetSection("BlobConfiguration").Bind(config);
        config.ReloadOnChange = true;
        config.BlobName = "settings.json";
        config.ReloadInterval = 10_000;
    },
    logger
);

// Registers and listens to updates of a single blob file in a folder
builder.Configuration.AddJsonBlob(config =>
    {
        builder.Configuration.GetSection("BlobConfiguration").Bind(config);
        config.ReloadOnChange = true;
        config.BlobName = "Folder/settings.json";
    },
    logger
);

// Registers and listens to updates of all blob files that are in the blob container right now
builder.Configuration.AddAllJsonBlobsInContainer(config =>
    {
        builder.Configuration.GetSection("BlobConfiguration").Bind(config);
        config.ReloadOnChange = true;
    },
    logger
);

// Registers and listens to updates of the blob container (when files are added at runtime, they are listened to as well)
builder.Configuration.AddJsonBlobContainerFolder(config =>
    {
        builder.Configuration.GetSection("BlobConfiguration").Bind(config);
        config.ReloadOnChange = true;
        config.Prefix = "ExecutorSettings"; // Folder Name
    },
    loggerFactory.CreateLogger<BlobContainerFolderProvider>()
);

builder.Services.Configure<ExampleOptions>(builder.Configuration.GetSection("ExampleSettings"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/config/{section}", (string sectionKey, IConfiguration config) =>
{
    var section = config.GetSection(sectionKey);
    return Results.Ok(new
    {
        exists = section.Exists(),
        value = section.Value
    });
})
.WithName("GetConfiguration")
.WithOpenApi();

app.Run();