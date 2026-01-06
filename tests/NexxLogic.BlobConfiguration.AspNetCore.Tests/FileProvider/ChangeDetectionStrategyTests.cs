using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NSubstitute;
using System.Collections.Concurrent;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider.ChangeDetectionStrategies;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class ChangeDetectionStrategyTests
{
    private const string BlobPath = "test/settings.json";
    private readonly ILogger<ETagChangeDetectionStrategy> _etagLogger = new NullLogger<ETagChangeDetectionStrategy>();
    private readonly ILogger<ContentBasedChangeDetectionStrategy> _contentLogger = new NullLogger<ContentBasedChangeDetectionStrategy>();

    [Theory]
    [InlineData("etag1", "etag2", true)] // Different ETags should detect change
    [InlineData("etag1", "etag1", false)] // Same ETag should not detect change
    public async Task ETagChangeDetectionStrategy_ShouldDetectChange_BasedOnETagComparison(string firstETag, string secondETag, bool shouldDetectChange)
    {
        // Arrange
        var strategy = new ETagChangeDetectionStrategy(_etagLogger);
        var contentHashes = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup first call
        SetupBlobProperties(blobClient, new ETag($"\"{firstETag}\""), DateTime.UtcNow, 1024);
        
        // Act - First call (should store initial ETag)
        var firstResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Setup second call
        SetupBlobProperties(blobClient, new ETag($"\"{secondETag}\""), DateTime.UtcNow, 1024);
        
        // Act - Second call
        var secondResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Assert
        Assert.True(firstResult); // First call always returns true (initial state)
        Assert.Equal(shouldDetectChange, secondResult);
    }

    [Theory]
    [InlineData("{ \"setting\": \"value1\" }", "{ \"setting\": \"value2\" }", true)] // Different content should detect change
    [InlineData("{ \"setting\": \"value1\" }", "{ \"setting\": \"value1\" }", false)] // Same content should not detect change
    public async Task ContentBasedChangeDetectionStrategy_ShouldDetectChange_BasedOnContentComparison(string firstContent, string secondContent, bool shouldDetectChange)
    {
        // Arrange
        var strategy = new ContentBasedChangeDetectionStrategy(_contentLogger, 5);
        var contentHashes = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup first content
        var content1 = System.Text.Encoding.UTF8.GetBytes(firstContent);
        SetupBlobProperties(blobClient, new ETag("\"etag1\""), DateTime.UtcNow, content1.Length);
        SetupBlobContentStream(blobClient, content1);

        // Act - First call
        var firstResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Setup second content (same or different based on test case)
        var content2 = System.Text.Encoding.UTF8.GetBytes(secondContent);
        SetupBlobProperties(blobClient, new ETag("\"etag2\""), DateTime.UtcNow, content2.Length);
        SetupBlobContentStream(blobClient, content2);

        // Act - Second call
        var secondResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Assert
        Assert.True(firstResult); // First call always returns true (initial state)
        Assert.Equal(shouldDetectChange, secondResult);
    }

    [Fact]
    public async Task ContentBasedChangeDetectionStrategy_ShouldFallbackToETag_WhenFileIsTooLarge()
    {
        // Arrange
        var maxFileSizeMb = 1; // 1 MB limit
        var strategy = new ContentBasedChangeDetectionStrategy(_contentLogger, maxFileSizeMb);
        var contentHashes = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup large file (2 MB, exceeds 1 MB limit)
        const int largeSizeBytes = 2 * 1024 * 1024;
        SetupBlobProperties(blobClient, new ETag("\"etag1\""), DateTime.UtcNow, largeSizeBytes);

        // Act - First call
        var firstResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Setup different ETag for large file
        SetupBlobProperties(blobClient, new ETag("\"etag2\""), DateTime.UtcNow, largeSizeBytes);

        // Act - Second call (should use ETag fallback)
        var secondResult = await strategy.HasChangedAsync(blobClient, BlobPath, contentHashes, cancellationToken);

        // Assert
        Assert.True(firstResult); // First call always returns true
        Assert.True(secondResult); // Should detect change via ETag fallback
    }

    [Theory]
    [InlineData("path/to/file.json")]
    [InlineData("simple.json")]
    [InlineData("deeply/nested/path/config.json")]
    public async Task ChangeDetectionStrategies_ShouldWorkWithDifferentPaths(string blobPath)
    {
        // Arrange
        var etagStrategy = new ETagChangeDetectionStrategy(_etagLogger);
        var contentStrategy = new ContentBasedChangeDetectionStrategy(_contentLogger, 5);
        var contentHashes = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup blob
        var content = "{ \"test\": \"data\" }"u8.ToArray();
        SetupBlobProperties(blobClient, new ETag("\"etag1\""), DateTime.UtcNow, content.Length);
        SetupBlobContentStream(blobClient, content);

        // Act
        var etagResult = await etagStrategy.HasChangedAsync(blobClient, blobPath, contentHashes, cancellationToken);
        var contentResult = await contentStrategy.HasChangedAsync(blobClient, blobPath, contentHashes, cancellationToken);

        // Assert
        Assert.True(etagResult);
        Assert.True(contentResult);
    }

    private static BlobClient CreateMockBlobClient()
    {
        return Substitute.For<BlobClient>();
    }

    private static void SetupBlobProperties(BlobClient blobClient, ETag etag, DateTime lastModified, long contentLength)
    {
        var properties = BlobsModelFactory.BlobProperties(
            lastModified: new DateTimeOffset(lastModified),
            contentLength: contentLength,
            eTag: etag);

        blobClient.GetPropertiesAsync(Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(properties, Substitute.For<Response>()));
    }

    private static void SetupBlobContentStream(BlobClient blobClient, byte[] content)
    {
        // Return a fresh stream for each call to handle multiple reads
        blobClient.OpenReadAsync(Arg.Any<long>(), Arg.Any<int?>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(content)));
    }
}