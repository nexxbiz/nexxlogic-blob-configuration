using Microsoft.Extensions.FileProviders;
using System.Collections;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobDirectoryContents : IDirectoryContents
{
    private readonly bool exists;
    private readonly List<IFileInfo> fileInfos;

    public BlobDirectoryContents(bool containerExists, List<IFileInfo> fileInfos)
    {
        this.exists = containerExists;
        this.fileInfos = fileInfos;
    }

    public bool Exists => exists;

    public IEnumerator<IFileInfo> GetEnumerator()
    {
        return fileInfos.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}