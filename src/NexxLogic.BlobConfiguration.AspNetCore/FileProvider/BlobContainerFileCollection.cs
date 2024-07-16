using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Nodes;
using System.Text;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobContainerFileCollection : IFileInfo
{
    private readonly IBlobContainerClientFactory clientFactory;
    private readonly string? prefix;
    private byte[]? folderBytes;
    private long lastModified;
    private readonly List<string> blobFileNames = new();

    public BlobContainerFileCollection(IBlobContainerClientFactory clientFactory, string? prefix = null)
    {
        try
        {
            var client = clientFactory.GetBlobContainerClient();
            IsDirectory = true;
            Exists = true;
            Name = !string.IsNullOrWhiteSpace(prefix) 
                ? $"{client.Name}/{prefix}" 
                : client.Name;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            Exists = false;
            Name = string.Empty;
        }

        this.clientFactory = clientFactory;
        this.prefix = prefix;

        lastModified = DateTimeOffset.UtcNow.Ticks;
    }

    public bool Exists { get; }

    public bool IsDirectory { get; }

    public DateTimeOffset LastModified => new(lastModified, TimeSpan.Zero);

    public long Length { get; }

    public string Name { get; }

    public string? PhysicalPath { get; }

    public Stream CreateReadStream()
    {
        if (folderBytes is null)
        {
            throw new InvalidOperationException("Folder is not loaded; there are no bytes to read.");
        }

        return new MemoryStream(folderBytes);
    }

    internal bool FolderContainsBlobFile(string blobFileName) => blobFileNames.Contains(blobFileName);

    internal void Load()
    {
        blobFileNames.Clear();
        folderBytes = null;

        var containerClient = clientFactory.GetBlobContainerClient();
        var result = new JsonObject();
                
        foreach (var blobInfoPage in containerClient.GetBlobs(prefix: prefix).AsPages())
        {
            foreach (var blobInfo in blobInfoPage.Values)
            {
                var blobClient = containerClient.GetBlobClient(blobInfo.Name);
                var properties = blobClient.GetProperties();
                lastModified = Math.Max(properties.Value.LastModified.Ticks, lastModified);
                blobFileNames.Add(blobInfo.Name);

                var blob = new BlobFileInfo(blobClient);

                using var jsonStream = blob.CreateReadStream();
                var jsonFileObject = JsonNode.Parse(jsonStream)?.AsObject()
                    ?? throw new InvalidOperationException($"Blob '{blob.Name}' does not contain a valid JSON object");

                jsonFileObject.ToList().ForEach(p =>
                {
                    if (p.Value is not null)
                    {
                        result.Add(p.Key, JsonNode.Parse(
                            p.Value!.ToJsonString()
                        ));
                    }
                });
            }
        }

        var jsonString = result.ToJsonString();
        folderBytes = Encoding.UTF8.GetBytes(jsonString);        
    }   
}