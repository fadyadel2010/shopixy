using System.Net;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// Feed-specific structural readiness for URLs emitted to external crawlers.
/// This class never performs DNS, HEAD, GET, or any other outbound network work.
/// </summary>
public static class FeedPublicUrlReadiness
{
    public const string InvalidProductUrl = "invalid_product_url";
    public const string InvalidPrimaryImageUrl = "invalid_primary_image_url";
    public const string InvalidAdditionalImageUrl = "invalid_additional_image_url";
    public const string NonPublicStorefrontUrl = "non_public_storefront_url";

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    private static readonly HashSet<string> SignedUrlQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "AWSAccessKeyId",
        "Expires",
        "GoogleAccessId",
        "Key-Pair-Id",
        "Policy",
        "Signature",
        "X-Amz-Credential",
        "X-Amz-Expires",
        "X-Amz-Signature",
        "X-Goog-Credential",
        "X-Goog-Expires",
        "X-Goog-Signature",
        "se",
        "sig"
    };

    public static bool IsPublicStorefrontBaseUrl(string? baseUrl, out string reasonCode)
    {
        if (!TryCreateHttpsPublicUri(baseUrl, requireOriginOnly: true, out _, out reasonCode))
        {
            reasonCode = NonPublicStorefrontUrl;
            return false;
        }

        return true;
    }

    public static bool IsPublicProductUrl(
        string? productUrl,
        string expectedStorefrontBaseUrl,
        string productSlug,
        out string reasonCode)
    {
        reasonCode = InvalidProductUrl;

        if (!IsPublicStorefrontBaseUrl(expectedStorefrontBaseUrl, out _))
        {
            reasonCode = NonPublicStorefrontUrl;
            return false;
        }

        if (string.IsNullOrWhiteSpace(productSlug) ||
            productSlug.Any(char.IsControl) ||
            productSlug.Any(char.IsWhiteSpace) ||
            productSlug.Contains('/') ||
            productSlug.Contains('\\') ||
            productSlug.Contains('?') ||
            productSlug.Contains('#'))
        {
            return false;
        }

        if (!TryCreateHttpsPublicUri(productUrl, requireOriginOnly: false, out var uri, out _))
        {
            return false;
        }

        var expectedBase = new Uri(expectedStorefrontBaseUrl, UriKind.Absolute);
        if (!SameOrigin(uri, expectedBase))
        {
            return false;
        }

        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        return string.Equals(decodedPath, $"/products/{productSlug.Trim()}", StringComparison.Ordinal);
    }

    public static bool IsPublicImageUrl(string? imageUrl, out string reasonCode)
    {
        reasonCode = InvalidPrimaryImageUrl;

        if (!TryCreateHttpsPublicUri(imageUrl, requireOriginOnly: false, out var uri, out _))
        {
            return false;
        }

        if (HasSignedUrlQuery(uri))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        return !string.IsNullOrWhiteSpace(extension) && SupportedImageExtensions.Contains(extension);
    }

    private static bool TryCreateHttpsPublicUri(
        string? value,
        bool requireOriginOnly,
        out Uri uri,
        out string reasonCode)
    {
        uri = null!;
        reasonCode = InvalidProductUrl;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri!) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !IsPublicHost(uri.Host))
        {
            return false;
        }

        if (requireOriginOnly &&
            (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
        {
            return false;
        }

        return true;
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static bool IsPublicHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized == "localhost" ||
            normalized.EndsWith(".localhost", StringComparison.Ordinal) ||
            normalized.EndsWith(".local", StringComparison.Ordinal) ||
            normalized.EndsWith(".lan", StringComparison.Ordinal) ||
            normalized.EndsWith(".internal", StringComparison.Ordinal) ||
            normalized.Contains('_'))
        {
            return false;
        }

        if (IPAddress.TryParse(normalized, out var address))
        {
            return IsPublicIpAddress(address);
        }

        return normalized.Contains('.');
    }

    private static bool IsPublicIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 => false,
                10 => false,
                100 when bytes[1] >= 64 && bytes[1] <= 127 => false,
                127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                192 when bytes[1] == 168 => false,
                _ => true
            };
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal &&
                   !address.IsIPv6SiteLocal &&
                   !address.IsIPv6Multicast &&
                   (bytes[0] & 0xfe) != 0xfc;
        }

        return false;
    }

    private static bool HasSignedUrlQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = parameter.IndexOf('=');
            var key = equalsIndex >= 0 ? parameter[..equalsIndex] : parameter;
            if (SignedUrlQueryKeys.Contains(Uri.UnescapeDataString(key)))
            {
                return true;
            }
        }

        return false;
    }
}
