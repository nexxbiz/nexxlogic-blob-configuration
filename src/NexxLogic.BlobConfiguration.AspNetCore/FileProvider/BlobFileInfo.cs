using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.FileProviders;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobFileInfo : IFileInfo
{
    private readonly BlobClient _blobClient;

    public BlobFileInfo(BlobClient blobClient)
    {
        _blobClient = blobClient;

        try
        {
            var properties = blobClient.GetProperties().Value;

            Exists = true;
            Name = blobClient.Name;
            Length = properties.ContentLength;
            LastModified = properties.LastModified;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            Exists = false;
            Name = string.Empty;
        }
    }

    public bool Exists { get; }
    public bool IsDirectory { get; }
    public DateTimeOffset LastModified { get; }
    public long Length { get; }
    public string Name { get; }
    public string? PhysicalPath { get; }

    public Stream CreateReadStream() =>
        _blobClient.DownloadStreaming().Value.Content;
}