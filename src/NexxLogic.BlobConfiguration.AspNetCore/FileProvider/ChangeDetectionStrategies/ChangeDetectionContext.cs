using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Context containing all the information needed for change detection strategies
/// </summary>
public class ChangeDetectionContext
{
    public BlobClient BlobClient { get; init; } = null!;
    public string BlobPath { get; init; } = null!;
    public BlobProperties Properties { get; init; } = null!;
    public ConcurrentDictionary<string, string> BlobFingerprints { get; init; } = null!;
    public CancellationToken CancellationToken { get; init; }
}
