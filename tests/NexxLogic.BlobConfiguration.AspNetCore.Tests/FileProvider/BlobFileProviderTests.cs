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
    private const string BlobName = "settings.json";
    private const int DefaultContentLength = 123;
    private static readonly DateTimeOffset DefaultLastModified = new(2022, 12, 19, 1, 1, 1, default);      

    [Fact]
    public void GetFileInfo_ShouldReturnCorrectFileInfo_WhenBlobExists()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        blobClientMock
            .Name
            .Returns(BlobName);

        // Act
        var result = sut.GetFileInfo(BlobName);

        // Assert
        Assert.True(result.Exists);
        Assert.Equal(DefaultLastModified, result.LastModified);
        Assert.Equal(DefaultContentLength, result.Length);
        Assert.Equal(BlobName, result.Name);
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
        var result = sut.GetFileInfo(BlobName);

        // Assert
        Assert.False(result.Exists);
        Assert.Empty(result.Name);
    }

    [Fact]
    public async Task Watch_ShouldRaiseChange_WhenNewVersionIsAvailable()
    {
        // Arrange
        var sut = CreateSut(out var blobClientMock);
        sut.GetFileInfo(BlobName);

        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: DefaultLastModified.Add(TimeSpan.FromSeconds(30)),
            contentLength: DefaultContentLength
        );

        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        blobClientMock
            .GetPropertiesAsync(null, changeToken.CancellationToken)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        // Act
        await Task.Delay(500);

        // Assert
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public async Task Watch_ShouldNotRetrieveProperties_WhenLoadIsPending()
    {
        // Act
        var sut = CreateSut(out var blobClientMock);
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
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
        sut.GetFileInfo(BlobName);        

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
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
        sut.GetFileInfo(BlobName);

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
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
        sut.GetFileInfo(BlobName);

        // Act
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(100);

        // Assert
        var existCalls = blobClientMock.ReceivedCalls().Where(x =>
        {
            return x.GetMethodInfo().Name == nameof(BlobClient.Exists);
        });
        Assert.True(existCalls.Count() > 1);
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
        sut.GetFileInfo(BlobName);
        
        var changeToken = (BlobChangeToken)sut.Watch(BlobName);
        await Task.Delay(20);

        // Pre-Assert
        blobClientMock
            .Received()
            .Exists(changeToken.CancellationToken);
        Assert.False(changeToken.HasChanged);

        // Act
        blobClientMock            
            .Exists(changeToken.CancellationToken)
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        await Task.Delay(30); // making sure that exists method returns with a TRUE value

        // Assert
        Assert.True(changeToken.HasChanged);
    }

    static BlobFileProvider CreateSut(out BlobClient blobClientMock, BlobConfigurationOptions? options = null)
    {
        var blobProperties = BlobsModelFactory.BlobProperties(
            lastModified: DefaultLastModified,
            contentLength: DefaultContentLength
        );

        blobClientMock = Substitute.For<BlobClient>();
        blobClientMock
            .GetProperties(default, default)
            .Returns(Response.FromValue(blobProperties, Substitute.For<Response>()));

        var blobClientFactoryMock = Substitute.For<IBlobClientFactory>();
        blobClientFactoryMock
            .GetBlobClient(BlobName)
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