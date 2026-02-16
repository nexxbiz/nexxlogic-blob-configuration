using Microsoft.Extensions.FileProviders;
using System.Collections;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

public class BlobDirectoryContents(bool containerExists, List<IFileInfo> fileInfos) : IDirectoryContents
{
    public bool Exists => containerExists;

    public IEnumerator<IFileInfo> GetEnumerator()
    {
        return fileInfos.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}