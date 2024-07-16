using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.FileProviders;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobContainerFolderInfo : IFileInfo
{
    private readonly string? folderName;
    private byte[]? folderBytes;
    private long lastModified;
    private readonly List<string> blobFileNames = new();

    public BlobContainerFolderInfo(string containerName, string? folderName = null)
    {
        try
        {
            IsDirectory = true;
            Exists = true;
            Name = !string.IsNullOrWhiteSpace(folderName) 
                ? $"{containerName}/{folderName}" 
                : containerName;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            Exists = false;
            Name = string.Empty;
        }

        this.folderName = folderName;

        lastModified = DateTimeOffset.UtcNow.Ticks;
    }

    public bool Exists { get; }
    public bool IsDirectory { get; }
    public DateTimeOffset LastModified => new(lastModified, TimeSpan.Zero);
    public long Length { get; }
    public string Name { get; }
    public string? PhysicalPath { get; }

    internal string? FolderName => folderName;

    internal bool FolderContainsBlobFile(string blobFileName) => blobFileNames.Contains(blobFileName);

    internal void ResetFolder()
    {
        folderBytes = null;
        blobFileNames.Clear();
        lastModified = 0;
    }

    internal void PopulateFolderContent(byte[] bytes, IEnumerable<string> blobFileNames, long lastModified)
    {
        folderBytes = bytes;
        this.blobFileNames.AddRange(blobFileNames);
        this.lastModified = lastModified;
    }

    public Stream CreateReadStream()
    {
        if (folderBytes is null)
        {
            throw new InvalidOperationException("Folder is not loaded; there are no bytes to read.");
        }

        return new MemoryStream(folderBytes);
    }
}