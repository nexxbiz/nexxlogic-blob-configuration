﻿using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Primitives;
using System.Net;

namespace BlobConfigurationProvider.Tests;

public class BlobFileProviderTests
{
    private readonly BlobFileProvider _sut;

    private readonly Mock<BlobClient> _blobClientMock = new();

    private readonly BlobConfigurationOptions _blobConfig;
    private const string BLOB_NAME = "settings.json";
    private const int DEFAULT_CONTENT_LENGTH = 123;
    private readonly DateTimeOffset _defaultLastModified = new DateTimeOffset(2022, 12, 19, 1, 1, 1, default);

    public BlobFileProviderTests()
    {
        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: _defaultLastModified,
            contentLength: DEFAULT_CONTENT_LENGTH
        );
        _blobClientMock
            .Setup(_ => _.GetProperties(default, default))
            .Returns(Response.FromValue(blobProperties, Mock.Of<Response>()));

        var blobClientFactoryMock = new Mock<IBlobClientFactory>();
        blobClientFactoryMock
            .Setup(_ => _.GetBlobClient(BLOB_NAME))
            .Returns(_blobClientMock.Object);

        _blobConfig = new BlobConfigurationOptions
        {
            ReloadInterval = 1
        };
        _sut = new BlobFileProvider(blobClientFactoryMock.Object, _blobConfig);
    }

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobExists()
    {
        // Arrange
        _blobClientMock
            .SetupGet(_ => _.Name)
            .Returns(BLOB_NAME);

        // Act
        var result = _sut.GetFileInfo(BLOB_NAME);

        // Assert
        result.Exists.Should().BeTrue();
        result.LastModified.Should().Be(_defaultLastModified);
        result.Length.Should().Be(DEFAULT_CONTENT_LENGTH);
        result.Name.Should().Be(BLOB_NAME);
    }

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobDoesNotExist()
    {
        // Arrange
        _blobClientMock
            .Setup(_ => _.GetProperties(default, default))
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));

        // Act
        var result = _sut.GetFileInfo(BLOB_NAME);

        // Assert
        result.Exists.Should().BeFalse();
        result.Name.Should().BeEmpty();
    }

    [Fact]
    public async Task Watch_ShouldRaiseChange_WhenNewVersionIsAvailable()
    {
        // Arrange
        _sut.GetFileInfo(BLOB_NAME);

        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: _defaultLastModified.Add(TimeSpan.FromSeconds(30)),
            contentLength: DEFAULT_CONTENT_LENGTH
        );
        _blobClientMock
            .Setup(_ => _.GetPropertiesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(blobProperties, Mock.Of<Response>()));

        // Act
        var changeToken = _sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Assert
        changeToken.HasChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Watch_ShouldNotRetrieveProperties_WhenLoadIsPending()
    {
        // Act
        var changeToken = (BlobChangeToken)_sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Assert
        _blobClientMock
            .Verify(_ => _.GetPropertiesAsync(null, changeToken.CancellationToken), Times.Never);
    }

    [Fact]
    public async Task Watch_ShouldReturn_WhenCancellationIsRequested()
    {
        // Arrange
        _sut.GetFileInfo(BLOB_NAME);
        _blobConfig.ReloadInterval = 100_000;

        // Act
        var changeToken = (BlobChangeToken)_sut.Watch(BLOB_NAME);
        await Task.Delay(10);
        changeToken.OnReload();

        // Assert
        _blobClientMock
            .Verify(_ => _.GetPropertiesAsync(null, changeToken.CancellationToken), Times.Never);
    }

    [Fact]
    public async Task Watch_ShouldStopRunning_WhenBlobDoesNotExist()
    {
        // Arrange
        _blobClientMock
            .Setup(_ => _.GetProperties(default, default))
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        _sut.GetFileInfo(BLOB_NAME);

        // Act
        var changeToken = (BlobChangeToken)_sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Assert
        _blobClientMock
            .Verify(_ => _.GetPropertiesAsync(null, changeToken.CancellationToken), Times.Never);
    }

    [Fact]
    public void GetDirectoryCoontents_ShouldThrow()
    {
        // Act
        var method = () => _sut.GetDirectoryContents(BLOB_NAME);

        // Assert
        method.Should().Throw<NotImplementedException>();
    }
}
