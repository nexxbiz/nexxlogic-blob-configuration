using NexxLogic.BlobConfiguration.AspNetCore.Utilities;

namespace NexxLogic.BlobConfiguration.AspNetCore.Tests.Utilities;

public class SasTokenDetectorTests
{
    [Fact]
    public void HasSasToken_ShouldReturnTrue_ForValidSasUrl()
    {
        // Arrange
        const string sasUrl = "https://mystorageaccount.blob.core.windows.net/container?sv=2021-06-08&sig=abcd1234&se=2023-12-31T23:59:59Z&sp=r";

        // Act
        var result = SasTokenDetector.HasSasToken(sasUrl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasSasToken_ShouldReturnFalse_ForUrlWithoutSas()
    {
        // Arrange
        const string regularUrl = "https://mystorageaccount.blob.core.windows.net/container";

        // Act
        var result = SasTokenDetector.HasSasToken(regularUrl);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasSasToken_ShouldReturnFalse_ForUrlWithNonSasQueryParams()
    {
        // Arrange
        const string urlWithNonSasParams = "https://mystorageaccount.blob.core.windows.net/container?timeout=30&api-version=2021-06-08";

        // Act
        var result = SasTokenDetector.HasSasToken(urlWithNonSasParams);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasSasToken_ShouldReturnFalse_ForIncompleteSasToken()
    {
        // Arrange - missing required 'sig' parameter
        const string incompleteSasUrl = "https://mystorageaccount.blob.core.windows.net/container?sv=2021-06-08&se=2023-12-31T23:59:59Z&sp=r";

        // Act
        var result = SasTokenDetector.HasSasToken(incompleteSasUrl);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void HasSasToken_ShouldReturnFalse_ForInvalidUrls(string? url)
    {
        // Act
        var result = SasTokenDetector.HasSasToken(url);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasSasLikeParameters_ShouldReturnTrue_ForAnySasParameter()
    {
        // Arrange
        const string urlWithSasParam = "https://mystorageaccount.blob.core.windows.net/container?sv=2021-06-08";

        // Act
        var result = SasTokenDetector.HasSasLikeParameters(new Uri(urlWithSasParam));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasSasLikeParameters_ShouldReturnFalse_ForNonSasParameters()
    {
        // Arrange
        const string urlWithNonSasParams = "https://mystorageaccount.blob.core.windows.net/container?timeout=30&custom=value";

        // Act
        var result = SasTokenDetector.HasSasLikeParameters(new Uri(urlWithNonSasParams));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasSasToken_WithUri_ShouldWorkConsistentlyWithStringVersion()
    {
        // Arrange
        const string sasUrl = "https://mystorageaccount.blob.core.windows.net/container?sv=2021-06-08&sig=abcd1234&se=2023-12-31T23:59:59Z";
        var uri = new Uri(sasUrl);

        // Act
        var stringResult = SasTokenDetector.HasSasToken(sasUrl);
        var uriResult = SasTokenDetector.HasSasToken(uri);

        // Assert
        Assert.Equal(stringResult, uriResult);
        Assert.True(stringResult); // Both should be true for valid SAS
    }

    [Fact]
    public void HasSasToken_ShouldBeCaseInsensitive()
    {
        // Arrange - SAS parameters in different cases
        const string sasUrlUpperCase = "https://mystorageaccount.blob.core.windows.net/container?SV=2021-06-08&SIG=abcd1234";
        const string sasUrlLowerCase = "https://mystorageaccount.blob.core.windows.net/container?sv=2021-06-08&sig=abcd1234";

        // Act
        var upperResult = SasTokenDetector.HasSasToken(sasUrlUpperCase);
        var lowerResult = SasTokenDetector.HasSasToken(sasUrlLowerCase);

        // Assert
        Assert.True(upperResult);
        Assert.True(lowerResult);
        Assert.Equal(upperResult, lowerResult);
    }
}