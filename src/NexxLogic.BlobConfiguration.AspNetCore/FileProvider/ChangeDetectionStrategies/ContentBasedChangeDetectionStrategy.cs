using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

/// <summary>
/// Content-based change detection strategy using SHA256 hashing (accurate, content-based)
/// </summary>
public class ContentBasedChangeDetectionStrategy(ILogger logger) : IChangeDetectionStrategy
{
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
                    : (previousHash.Length >= 8
                        ? previousHash[..8] + "..."
                        : previousHash);

                var newHashDisplay = currentHash.Length >= 8
                    ? currentHash[..8] + "..."
                    : currentHash;

                logger.LogInformation("Content change detected for blob {BlobPath}. Hash changed from {OldHash} to {NewHash}",
                    context.BlobPath, oldHashDisplay, newHashDisplay);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check content changes for blob {BlobPath}", context.BlobPath);
            return false;
        }
    }
}