using Azure.Storage.Blobs;
using NexxLogic.BlobConfiguration.AspNetCore.Factories;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Factories;

public class BlobClientFactoryTests
{
    private readonly IBlobContainerClientFactory _blobContainerFactory;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly BlobClient _expectedBlobClient;
    private readonly BlobClientFactory _sut;

    private const string TestBlobName = "settings.json";

    public BlobClientFactoryTests()
    {
        _blobContainerFactory = Substitute.For<IBlobContainerClientFactory>();
        _blobContainerClient = Substitute.For<BlobContainerClient>();
        _expectedBlobClient = Substitute.For<BlobClient>();
        
        _sut = new BlobClientFactory(_blobContainerFactory);
    }

    [Fact]
    public void GetBlobClient_ShouldReturnBlobClient_WhenValidBlobNameProvided()
    {
        // Arrange
        _blobContainerFactory
            .GetBlobContainerClient()
            .Returns(_blobContainerClient);
        
        _blobContainerClient
            .GetBlobClient(TestBlobName)
            .Returns(_expectedBlobClient);

        // Act
        var result = _sut.GetBlobClient(TestBlobName);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_expectedBlobClient, result);
        
        // Verify interaction sequence
        Received.InOrder(() =>
        {
            _blobContainerFactory.GetBlobContainerClient();
            _blobContainerClient.GetBlobClient(TestBlobName);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid/blob/name")]
    public void GetBlobClient_ShouldReturnBlobClient_EvenWithInvalidBlobNames(string? blobName)
    {
        // Arrange
        var mockBlobClient = Substitute.For<BlobClient>();
        _blobContainerFactory
            .GetBlobContainerClient()
            .Returns(_blobContainerClient);
        
        _blobContainerClient
            .GetBlobClient(blobName)
            .Returns(mockBlobClient);

        // Act
        var result = _sut.GetBlobClient(blobName!);

        // Assert - Azure SDK allows any blob name, validation happens at operation time
        Assert.NotNull(result);
        Assert.Same(mockBlobClient, result);
        
        // Verify the factory was called with the provided name
        _blobContainerFactory.Received(1).GetBlobContainerClient();
        _blobContainerClient.Received(1).GetBlobClient(blobName);
    }

    [Fact]
    public void GetBlobClient_ShouldPropagateException_WhenContainerFactoryThrows()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Container not available");
        _blobContainerFactory
            .GetBlobContainerClient()
            .Throws(expectedException);

        // Act & Assert
        var actualException = Assert.Throws<InvalidOperationException>(() => 
            _sut.GetBlobClient(TestBlobName));
            
        Assert.Same(expectedException, actualException);
    }
}