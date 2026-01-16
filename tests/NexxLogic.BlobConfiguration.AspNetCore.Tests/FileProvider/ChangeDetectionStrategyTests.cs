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
    private const string DefaultBlobPath = "test/settings.json";
    private readonly ILogger<ETagChangeDetectionStrategy> _etagLogger = new NullLogger<ETagChangeDetectionStrategy>();
    private readonly ILogger<ContentBasedChangeDetectionStrategy> _contentLogger = new NullLogger<ContentBasedChangeDetectionStrategy>();

    [Theory]
    [InlineData("etag1", "etag2", true)] // Different ETags should detect change
    [InlineData("etag1", "etag1", false)] // Same ETag should not detect change
    public async Task ETagChangeDetectionStrategy_ShouldDetectChange_BasedOnETagComparison(string firstETag, string secondETag, bool shouldDetectChange)
    {
        // Arrange
        var strategy = new ETagChangeDetectionStrategy(_etagLogger);
        var (firstContext, secondContext) = CreateETagTestContexts(firstETag, secondETag);
        
        // Act
        var firstResult = await strategy.HasChangedAsync(firstContext);
        var secondResult = await strategy.HasChangedAsync(secondContext);

        // Assert
        AssertChangeDetectionResults(firstResult, secondResult, shouldDetectChange);
    }

    [Theory]
    [InlineData("{ \"setting\": \"value1\" }", "{ \"setting\": \"value2\" }", true)] // Different content should detect change
    [InlineData("{ \"setting\": \"value1\" }", "{ \"setting\": \"value1\" }", false)] // Same content should not detect change
    public async Task ContentBasedChangeDetectionStrategy_ShouldDetectChange_BasedOnContentComparison(string firstContent, string secondContent, bool shouldDetectChange)
    {
        // Arrange
        var strategy = new ContentBasedChangeDetectionStrategy(_contentLogger);
        var (firstContext, secondContext) = CreateContentTestContexts(firstContent, secondContent);

        // Act
        var firstResult = await strategy.HasChangedAsync(firstContext);
        var secondResult = await strategy.HasChangedAsync(secondContext);

        // Assert
        AssertChangeDetectionResults(firstResult, secondResult, shouldDetectChange);
    }

    [Fact]
    public async Task SizeLimitedChangeDetectionDecorator_ShouldFallbackToETag_WhenFileIsTooLarge()
    {
        // Arrange
        const int maxFileSizeMb = 1; // 1 MB limit
        const int largeSizeBytes = 2 * 1024 * 1024; // 2 MB, exceeds limit
        
        var decorator = CreateSizeLimitedDecorator(maxFileSizeMb);
        var (firstContext, secondContext) = CreateLargeFileTestContexts(largeSizeBytes, "etag1", "etag2");

        // Act
        var firstResult = await decorator.HasChangedAsync(firstContext);
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
        var context = CreateTestContext(blobPath, "{ \"test\": \"data\" }", "etag1");

        // Act
        var etagResult = await etagStrategy.HasChangedAsync(context);
        var contentResult = await contentStrategy.HasChangedAsync(context);

        // Assert
        Assert.True(etagResult);
        Assert.True(contentResult);
    }

    // Helper methods to reduce repetition

    private (ChangeDetectionContext first, ChangeDetectionContext second) CreateETagTestContexts(string firstETag, string secondETag)
    {
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();

        var firstProperties = CreateBlobProperties(firstETag, 1024);
        var secondProperties = CreateBlobProperties(secondETag, 1024);

        return (
            CreateChangeDetectionContext(blobClient, DefaultBlobPath, firstProperties, blobFingerprints),
            CreateChangeDetectionContext(blobClient, DefaultBlobPath, secondProperties, blobFingerprints)
        );
    }

    private (ChangeDetectionContext first, ChangeDetectionContext second) CreateContentTestContexts(string firstContent, string secondContent)
    {
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        
        // Use separate blob clients to avoid stream setup conflicts
        var blobClient1 = CreateMockBlobClient();
        var blobClient2 = CreateMockBlobClient();

        var content1 = System.Text.Encoding.UTF8.GetBytes(firstContent);
        var content2 = System.Text.Encoding.UTF8.GetBytes(secondContent);

        SetupBlobContentStream(blobClient1, content1);
        var firstProperties = CreateBlobProperties("etag1", content1.Length);
        var firstContext = CreateChangeDetectionContext(blobClient1, DefaultBlobPath, firstProperties, blobFingerprints);

        SetupBlobContentStream(blobClient2, content2);
        var secondProperties = CreateBlobProperties("etag2", content2.Length);
        var secondContext = CreateChangeDetectionContext(blobClient2, DefaultBlobPath, secondProperties, blobFingerprints);

        return (firstContext, secondContext);
    }

    private (ChangeDetectionContext first, ChangeDetectionContext second) CreateLargeFileTestContexts(long fileSize, string firstETag, string secondETag)
    {
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();

        var firstProperties = CreateBlobProperties(firstETag, fileSize);
        var secondProperties = CreateBlobProperties(secondETag, fileSize);

        return (
            CreateChangeDetectionContext(blobClient, DefaultBlobPath, firstProperties, blobFingerprints),
            CreateChangeDetectionContext(blobClient, DefaultBlobPath, secondProperties, blobFingerprints)
        );
    }

    private ChangeDetectionContext CreateTestContext(string blobPath, string content, string etag)
    {
        var blobFingerprints = new ConcurrentDictionary<string, string>();
        var blobClient = CreateMockBlobClient();
        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
        
        SetupBlobContentStream(blobClient, contentBytes);
        var properties = CreateBlobProperties(etag, contentBytes.Length);
        
        return CreateChangeDetectionContext(blobClient, blobPath, properties, blobFingerprints);
    }

    private static ChangeDetectionContext CreateChangeDetectionContext(
        BlobClient blobClient, 
        string blobPath, 
        BlobProperties properties, 
        ConcurrentDictionary<string, string> blobFingerprints)
    {
        return new ChangeDetectionContext
        {
            BlobClient = blobClient,
            BlobPath = blobPath,
            Properties = properties,
            BlobFingerprints = blobFingerprints,
            CancellationToken = CancellationToken.None
        };
    }

    private static BlobProperties CreateBlobProperties(string etag, long contentLength)
    {
        return BlobsModelFactory.BlobProperties(
            eTag: new ETag($"\"{etag}\""), 
            lastModified: DateTime.UtcNow, 
            contentLength: contentLength);
    }

    private SizeLimitedChangeDetectionDecorator CreateSizeLimitedDecorator(int maxFileSizeMb)
    {
        var contentStrategy = new ContentBasedChangeDetectionStrategy(_contentLogger);
        var etagStrategy = new ETagChangeDetectionStrategy(_etagLogger);
        return new SizeLimitedChangeDetectionDecorator(
            contentStrategy, etagStrategy, maxFileSizeMb, _etagLogger);
    }

    private static void AssertChangeDetectionResults(bool firstResult, bool secondResult, bool shouldDetectChange)
    {
        Assert.True(firstResult); // First call always returns true (initial state)
        Assert.Equal(shouldDetectChange, secondResult);
    }

    private static BlobClient CreateMockBlobClient()
    {
        return Substitute.For<BlobClient>();
    }


    private static void SetupBlobContentStream(BlobClient blobClient, byte[] content)
    {
        // Return a factory that creates a fresh stream for each call to handle multiple reads
        // The stream will be disposed by the consuming code (ContentBasedChangeDetectionStrategy uses 'await using')
        blobClient.OpenReadAsync(Arg.Any<long>(), Arg.Any<int?>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(content) { Position = 0 }));
    }
}