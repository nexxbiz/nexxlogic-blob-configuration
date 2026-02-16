using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Content-based change detection strategy using SHA256 hashing (accurate, content-based)
/// </summary>
public class ContentBasedChangeDetectionStrategy() : IChangeDetectionStrategy
{
    private readonly ILogger? _logger;
    
    public ContentBasedChangeDetectionStrategy(ILogger logger) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<bool> HasChangedAsync(ChangeDetectionContext context)
    {
        try
        {
            await using var stream = await context.BlobClient.OpenReadAsync(cancellationToken: context.CancellationToken);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, context.CancellationToken);
            var currentHash = Convert.ToBase64String(hashBytes);

            var sha256Key = $"{context.BlobPath}:sha256";
            var previousHash = context.BlobFingerprints.GetValueOrDefault(sha256Key);
            if (currentHash != previousHash)
            {
                context.BlobFingerprints[sha256Key] = currentHash;

                var oldHashDisplay = previousHash == null
                    ? "null"
                    : HashDisplay(previousHash);

                var newHashDisplay = HashDisplay(currentHash);

                _logger.LogInformation("Content change detected for blob {BlobPath}. Hash changed from {OldHash} to {NewHash}",
                    context.BlobPath, oldHashDisplay, newHashDisplay);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check content changes for blob {BlobPath}", context.BlobPath);
            return false;
        }
    }

    private static string HashDisplay(string hash)
    {
        return hash.Length >= 8
            ? hash[..8] + "..."
            : hash;
    }
}