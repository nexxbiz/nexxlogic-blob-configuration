using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup first call
        var firstProperties = BlobsModelFactory.BlobProperties(eTag: new ETag($"\"{firstETag}\""), lastModified: DateTime.UtcNow, contentLength: 1024);
        var firstContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = firstProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };
        
        // Act - First call (should store initial ETag)
        var firstResult = await strategy.HasChangedAsync(firstContext);

        // Setup second call
        var secondProperties = BlobsModelFactory.BlobProperties(eTag: new ETag($"\"{secondETag}\""), lastModified: DateTime.UtcNow, contentLength: 1024);
        var secondContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = secondProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };
        
        // Act - Second call
        var secondResult = await strategy.HasChangedAsync(secondContext);

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
        var strategy = new ContentBasedChangeDetectionStrategy(_contentLogger);
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup first content
        var content1 = System.Text.Encoding.UTF8.GetBytes(firstContent);
        var firstProperties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"etag1\""), lastModified: DateTime.UtcNow, contentLength: content1.Length);
        SetupBlobContentStream(blobClient, content1);

        var firstContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = firstProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };

        // Act - First call
        var firstResult = await strategy.HasChangedAsync(firstContext);

        // Setup second content (same or different based on test case)
        var content2 = System.Text.Encoding.UTF8.GetBytes(secondContent);
        var secondProperties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"etag2\""), lastModified: DateTime.UtcNow, contentLength: content2.Length);
        SetupBlobContentStream(blobClient, content2);

        var secondContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = secondProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };

        // Act - Second call
        var secondResult = await strategy.HasChangedAsync(secondContext);

        // Assert
        Assert.True(firstResult); // First call always returns true (initial state)
        Assert.Equal(shouldDetectChange, secondResult);
    }

    [Fact]
    public async Task SizeLimitedChangeDetectionDecorator_ShouldFallbackToETag_WhenFileIsTooLarge()
    {
        // Arrange
        var maxFileSizeMb = 1; // 1 MB limit
        var contentStrategy = new ContentBasedChangeDetectionStrategy(_contentLogger);
        var etagStrategy = new ETagChangeDetectionStrategy(_etagLogger);
        var decorator = new SizeLimitedChangeDetectionDecorator(
            contentStrategy, etagStrategy, maxFileSizeMb, _etagLogger);
        
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup large file (2 MB, exceeds 1 MB limit)
        const int largeSizeBytes = 2 * 1024 * 1024;
        var firstProperties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"etag1\""), lastModified: DateTime.UtcNow, contentLength: largeSizeBytes);

        var firstContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = firstProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };

        // Act - First call (should use ETag fallback due to large size)
        var firstResult = await decorator.HasChangedAsync(firstContext);

        // Setup different ETag for large file
        var secondProperties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"etag2\""), lastModified: DateTime.UtcNow, contentLength: largeSizeBytes);
        var secondContext = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = BlobPath,
            Properties = secondProperties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };

        // Act - Second call (should use ETag fallback and detect change)
        var secondResult = await decorator.HasChangedAsync(secondContext);

        // Assert
        Assert.True(firstResult); // First call always returns true (initial state)
        Assert.True(secondResult); // Should detect ETag change via fallback strategy
    }

    [Theory]
    [InlineData("path/to/file.json")]
    [InlineData("simple.json")]
    [InlineData("deeply/nested/path/config.json")]
    public async Task ChangeDetectionStrategies_ShouldWorkWithDifferentPaths(string blobPath)
    {
        // Arrange
        var etagStrategy = new ETagChangeDetectionStrategy(_etagLogger);
        var contentStrategy = new ContentBasedChangeDetectionStrategy(_contentLogger);
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var cancellationToken = CancellationToken.None;

        // Setup blob
        var content = "{ \"test\": \"data\" }"u8.ToArray();
        var properties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"etag1\""), lastModified: DateTime.UtcNow, contentLength: content.Length);
        SetupBlobContentStream(blobClient, content);

        var context = new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = blobPath,
            Properties = properties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = cancellationToken
        };

        // Act
        var etagResult = await etagStrategy.HasChangedAsync(context);
        var contentResult = await contentStrategy.HasChangedAsync(context);

        // Assert
        Assert.True(etagResult);
        Assert.True(contentResult);
    }

    private static BlobClient CreateMockBlobClient()
    {
        return Substitute.For<BlobClient>();
    }


    private static void SetupBlobContentStream(BlobClient blobClient, byte[] content)
    {
        // Return a fresh stream for each call to handle multiple reads
        blobClient.OpenReadAsync(Arg.Any<long>(), Arg.Any<int?>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(content)));
    }
}