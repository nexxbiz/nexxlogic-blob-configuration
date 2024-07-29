using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NexxLogic.BlobConfiguration.AspNetCore.FileProvider;
using NexxLogic.BlobConfiguration.AspNetCore.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.FileProvider;

public class BlobFileProviderTests
{   
    private const string BLOB_NAME = "settings.json";
    private const int DEFAULT_CONTENT_LENGTH = 123;
    private static readonly DateTimeOffset _defaultLastModified = new(2022, 12, 19, 1, 1, 1, default);      

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobExists()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .Name
            .Returns(BLOB_NAME);

        // Act
        var result = sut.GetFileInfo(BLOB_NAME);

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
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .GetProperties(default, default)
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));

        // Act
        var result = sut.GetFileInfo(BLOB_NAME);

        // Assert
        result.Exists.Should().BeFalse();
        result.Name.Should().BeEmpty();
    }

    [Fact]
    public async Task Watch_ShouldRaiseChange_WhenNewVersionIsAvailable()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        sut.GetFileInfo(BLOB_NAME);

        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: _defaultLastModified.Add(TimeSpan.FromSeconds(30)),
            contentLength: DEFAULT_CONTENT_LENGTH
        );

        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        blobClientMock
            .GetPropertiesAsync(null, changeToken.CancellationToken)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        // Act
        await Task.Delay(30);

        // Assert
        changeToken.HasChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Watch_ShouldNotRetrieveProperties_WhenLoadIsPending()
    {
        // Act
        var sut = CreateSut(out var blobClientMock);
        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Assert
        await blobClientMock
              .DidNotReceive()
              .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task Watch_ShouldReturn_WhenCancellationIsRequested()
    {
        // Arrange
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 100_000
        };
        var sut = CreateSut(out var blobClientMock, options);
        sut.GetFileInfo(BLOB_NAME);        

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        await Task.Delay(10);
        changeToken.OnReload();

        // Assert
        await blobClientMock
           .DidNotReceive()
           .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task When_BlobFile_Not_Optional_And_BlobDoesNotExist_Watch_ShouldStopRunning()
    {
        // Arrange        
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .GetProperties(default, default)
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        sut.GetFileInfo(BLOB_NAME);

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Assert
        await blobClientMock
            .DidNotReceive()
            .GetPropertiesAsync(null, changeToken.CancellationToken);
    }

    [Fact]
    public async Task When_BlobFile_Optional_And_BlobDoesNotExist_Watch_Should_Keep_Checking_For_Existence()
    {
        // Arrange        
        var options = new BlobConfigurationOptions
        {            
            ReloadInterval = 1,
            Optional = true
        };
        var sut = CreateSut(out var blobClientMock, options);

        blobClientMock
            .GetProperties(default, default)
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        sut.GetFileInfo(BLOB_NAME);

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        await Task.Delay(30);

        // Assert
        blobClientMock
            .Received()
            .Exists(changeToken.CancellationToken);
    }

    [Fact]
    public async Task When_BlobFile_Optional_And_File_Added_Later_Then_RaisesChange()
    {
        // Arrange        
        var options = new BlobConfigurationOptions
        {
            ReloadInterval = 1,
            Optional = true
        };
        var sut = CreateSut(out var blobClientMock, options);

        blobClientMock
            .GetProperties(default, default)
            .Throws(new RequestFailedException(
                (int)HttpStatusCode.NotFound,
                "Blob not found",
                BlobErrorCode.BlobNotFound.ToString(),
                null
            ));
        sut.GetFileInfo(BLOB_NAME);
        
        var changeToken = (BlobChangeToken)sut.Watch(BLOB_NAME);
        await Task.Delay(20);

        // Pre-Assert
        blobClientMock
            .Received()
            .Exists(changeToken.CancellationToken);
        changeToken.HasChanged.Should().BeFalse();

        // Act
        blobClientMock            
            .Exists(changeToken.CancellationToken)
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        await Task.Delay(30); // making sure that exists method returns with a TRUE value

        // Assert
        changeToken.HasChanged.Should().BeTrue();
    }

    static BlobFileProvider CreateSut(out BlobClient blobClientMock, BlobConfigurationOptions? options = null)
    {
        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: _defaultLastModified,
            contentLength: DEFAULT_CONTENT_LENGTH
        );

        blobClientMock = Substitute.For<BlobClient>();
        blobClientMock
            .GetProperties(default, default)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        blobClientFactoryMock
            .GetBlobClient(BLOB_NAME)
            .Returns(blobClientMock);

        var blobContainerClientMock = Substitute.For<BlobContainerClient>();
        var blobContainerClientFactoryMock = Substitute.For<IBlobContainerClientFactory>();
        blobContainerClientFactoryMock
            .GetBlobContainerClient()
            .Returns(blobContainerClientMock);

        var defaultBlobConfig = new BlobConfigurationOptions
        {
            ReloadInterval = 1
        };

        var loggerFactory = new NullLoggerFactory();
        var logger = loggerFactory.CreateLogger<BlobFileProvider>();

        return new BlobFileProvider(blobClientFactoryMock, blobContainerClientFactoryMock, options ?? defaultBlobConfig, logger);
    }
}
