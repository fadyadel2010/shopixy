using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.API.Infrastructure.RateLimiting;

/// <summary>
/// FEED-1B: Dedicated rate-limit policy for the anonymous public product feed
/// endpoints (GET /api/public/feeds/meta/{token} and, since FEED-2,
/// GET /api/public/feeds/google/{token}). One shared policy serves every feed
/// channel — no per-channel limiter duplication.
///
/// PARTITION KEY:
/// - channel + feed token (from the URL path — the token IS the credential)
///   plus caller IP, so one abusive caller cannot exhaust capacity for other
///   fetchers of the same feed, and one feed (or channel) cannot mask abuse
///   behind another.
/// - The raw token appears only inside the in-memory partition key. It is
///   never logged, never placed in response bodies, and never used as a
///   metric dimension.
/// - No tenant header is read: tenant identity is only trustworthy AFTER the
///   hashed-token lookup, which happens later in the pipeline.
///
/// POLICY:
/// - Fixed window, 30 requests/minute/channel+token+IP, no queue. Sized for
///   automated catalog fetchers (Meta/Google fetch periodically, not
///   continuously) plus manual testing; abusive bursts are rejected with the
///   repository's standard 429 response (<see cref="RateLimitRejectionHandlers"/>).
/// </summary>
public static class PublicFeedRateLimitPolicy
{
    public const string PolicyName = "PublicFeedRateLimit";

    public const int PermitLimit = 30;
    public const int WindowSeconds = 60;

    private const string MetaPathPrefix = "/api/public/feeds/meta/";
    private const string GooglePathPrefix = "/api/public/feeds/google/";
    private const string TikTokPathPrefix = "/api/public/feeds/tiktok/";
    private const string SnapchatPathPrefix = "/api/public/feeds/snapchat/";
    private const string UnknownChannel = "unknown";

    public static RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value;
        var channel = ExtractChannel(path);
        var token = ExtractToken(path);
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var ipPartition = string.IsNullOrWhiteSpace(clientIp)
            ? $"conn-{httpContext.Connection.Id}"
            : clientIp;

        var partitionKey = BuildPartitionKey(channel, token, ipPartition);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => CreateOptions());
    }

    /// <summary>Meta overload — keeps the certified Feed-1B partition key shape.</summary>
    public static string BuildPartitionKey(string token, string ipPartition) =>
        BuildPartitionKey("meta", token, ipPartition);

    /// <summary>
    /// FEED-2: channel is part of the partition so Meta and Google windows are
    /// independent. For channel "meta" the key is exactly the Feed-1B shape.
    /// </summary>
    public static string BuildPartitionKey(string channel, string token, string ipPartition) =>
        $"public-feed:{channel}:{FingerprintToken(token)}:ip:{ipPartition}";

    public static string FingerprintToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    public static FixedWindowRateLimiterOptions CreateOptions() => new()
    {
        PermitLimit = PermitLimit,
        Window = TimeSpan.FromSeconds(WindowSeconds),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
        AutoReplenishment = true
    };

    /// <summary>
    /// The feed channel of the request path ("meta"/"google"); "unknown" when
    /// the path does not target a public feed route.
    /// </summary>
    public static string ExtractChannel(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return UnknownChannel;
        }

        if (path.IndexOf(MetaPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "meta";
        }

        if (path.IndexOf(GooglePathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "google";
        }

        if (path.IndexOf(TikTokPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "tiktok";
        }

        if (path.IndexOf(SnapchatPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "snapchat";
        }

        return UnknownChannel;
    }

    /// <summary>
    /// The token is the last path segment after the channel route prefix.
    /// Whitespace is normalized to match the feed service's token handling;
    /// missing/empty segments collapse to a shared "unknown" partition.
    /// </summary>
    public static string ExtractToken(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "unknown";
        }

        var prefix = MatchedPrefix(path);
        if (prefix == null)
        {
            return "unknown";
        }

        var index = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        var token = path[(index + prefix.Length)..].Trim();
        var slash = token.IndexOf('/');
        if (slash >= 0)
        {
            token = token[..slash].Trim();
        }

        return token.Length == 0 ? "unknown" : token;
    }

    private static string? MatchedPrefix(string path)
    {
        if (path.IndexOf(MetaPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MetaPathPrefix;
        }

        if (path.IndexOf(GooglePathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return GooglePathPrefix;
        }

        if (path.IndexOf(TikTokPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return TikTokPathPrefix;
        }

        if (path.IndexOf(SnapchatPathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SnapchatPathPrefix;
        }

        return null;
    }
}
