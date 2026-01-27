using System.Web;

namespace NexxLogic.BlobConfiguration.AspNetCore.Utilities;

/// <summary>
/// Utility for detecting Azure Storage SAS tokens in URLs
/// </summary>
public static class SasTokenDetector
{
    /// <summary>
    /// Required SAS token parameters that indicate a valid SAS token
    /// </summary>
    private static readonly HashSet<string> RequiredSasParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "sv", // SAS version - always required
        "sig" // Signature - always required
    };
    
    /// <summary>
    /// Common SAS token parameters (not all required, but presence indicates SAS)
    /// </summary>
    private static readonly HashSet<string> CommonSasParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "sv",   // SAS version
        "sig",  // Signature
        "se",   // Expiry time
        "sr",   // Resource (blob, container, etc.)
        "sp",   // Permissions
        "st",   // Start time
        "spr",  // Protocol
        "sip",  // IP range
        "rscd", // Response content-disposition
        "rsct", // Response content-type
        "rsce", // Response content-encoding
        "rscl", // Response content-language
        "rscc"  // Response cache-control
    };

    /// <summary>
    /// Detects if a URL contains a SAS token by checking for required SAS parameters
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <returns>True if the URL contains a valid SAS token, false otherwise</returns>
    public static bool HasSasToken(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        try
        {
            var uri = new Uri(url);
            return HasSasToken(uri);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Detects if a URI contains a SAS token by checking for required SAS parameters
    /// </summary>
    /// <param name="uri">The URI to check</param>
    /// <returns>True if the URI contains a valid SAS token, false otherwise</returns>
    public static bool HasSasToken(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
            return false;

        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        
        // Check if all required SAS parameters are present
        return RequiredSasParameters.All(param => !string.IsNullOrEmpty(queryParams[param]));
    }

    /// <summary>
    /// Detects if a URI likely contains SAS-related parameters (more lenient check)
    /// </summary>
    /// <param name="uri">The URI to check</param>
    /// <returns>True if the URI contains SAS-like parameters, false otherwise</returns>
    public static bool HasSasLikeParameters(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
            return false;

        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        
        // Check if any common SAS parameters are present
        return CommonSasParameters.Any(param => !string.IsNullOrEmpty(queryParams[param]));
    }
}