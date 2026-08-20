using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Feeds;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-4 hardening parity: the shared Feed-1B safeguards extend to the
/// Snapchat channel without weakening Meta, Google, or TikTok — rate-limit
/// channel extraction/partition independence, and token redaction of the
/// snapchat public feed path in logs.
/// </summary>
public sealed class SnapchatFeedHardeningParityTests
{
    // =========================================================================
    // Rate limiting: channel-aware partitioning
    // =========================================================================

    [Theory]
    [InlineData("/api/public/feeds/meta/tok-a", "meta")]
    [InlineData("/api/public/feeds/google/tok-a", "google")]
    [InlineData("/api/public/feeds/tiktok/tok-a", "tiktok")]
    [InlineData("/api/public/feeds/snapchat/tok-a", "snapchat")]
    [InlineData("/API/PUBLIC/FEEDS/SNAPCHAT/tok-a", "snapchat")]
    [InlineData("/api/public/products", "unknown")]
    [InlineData(null, "unknown")]
    public void ExtractChannel_ReadsTheSnapchatChannelSegment(string? path, string expected)
    {
        Assert.Equal(expected, PublicFeedRateLimitPolicy.ExtractChannel(path));
    }

    [Theory]
    [InlineData("/api/public/feeds/snapchat/abc123", "abc123")]
    [InlineData("/api/public/feeds/snapchat/abc123/", "abc123")]
    [InlineData("/api/public/feeds/snapchat/ abc123 ", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/SNAPCHAT/abc123", "abc123")]
    public void ExtractToken_ReadsTheLastPathSegment_OnTheSnapchatRoute(string path, string expected)
    {
        Assert.Equal(expected, PublicFeedRateLimitPolicy.ExtractToken(path));
    }

    [Fact]
    public void MetaPartitionKeyShape_UsesFingerprint_Feed1BContractPreservedWithoutRawSecret()
    {
        var fingerprint = PublicFeedRateLimitPolicy.FingerprintToken("tok-a");

        Assert.Equal(
            $"public-feed:meta:{fingerprint}:ip:10.0.0.1",
            PublicFeedRateLimitPolicy.BuildPartitionKey("tok-a", "10.0.0.1"));
    }

    [Theory]
    [InlineData("meta", "tok-a", "10.0.0.1")]
    [InlineData("google", "tok-a", "10.0.0.1")]
    [InlineData("tiktok", "tok-a", "10.0.0.1")]
    [InlineData("snapchat", "tok-a", "10.0.0.1")]
    public void PartitionKey_SeparatesAllChannels_SameTokenNeverSharesAWindow(
        string channel, string token, string ip)
    {
        var key = PublicFeedRateLimitPolicy.BuildPartitionKey(channel, token, ip);
        var fingerprint = PublicFeedRateLimitPolicy.FingerprintToken(token);

        Assert.Equal($"public-feed:{channel}:{fingerprint}:ip:{ip}", key);
        Assert.DoesNotContain(token, key, StringComparison.Ordinal);
    }

    [Fact]
    public void AllChannelPartitions_AreIndependent_ForTheSameTokenAndIp()
    {
        var metaKey = PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.1");
        var googleKey = PublicFeedRateLimitPolicy.BuildPartitionKey("google", "tok-a", "10.0.0.1");
        var tiktokKey = PublicFeedRateLimitPolicy.BuildPartitionKey("tiktok", "tok-a", "10.0.0.1");
        var snapchatKey = PublicFeedRateLimitPolicy.BuildPartitionKey("snapchat", "tok-a", "10.0.0.1");

        Assert.Equal(4, new[] { metaKey, googleKey, tiktokKey, snapchatKey }.Distinct().Count());
        Assert.NotEqual(metaKey, snapchatKey);
        Assert.NotEqual(googleKey, snapchatKey);
        Assert.NotEqual(tiktokKey, snapchatKey);
    }

    [Fact]
    public void GetPartition_SnapchatRoute_PartitionsByChannelTokenAndIp()
    {
        var snapchat = CreateContext("/api/public/feeds/snapchat/tok-a", "10.0.0.1");
        var snapchatSame = CreateContext("/api/public/feeds/snapchat/tok-a", "10.0.0.1");
        var snapchatOtherIp = CreateContext("/api/public/feeds/snapchat/tok-a", "10.0.0.2");
        var snapchatOtherToken = CreateContext("/api/public/feeds/snapchat/tok-b", "10.0.0.1");
        var metaSameToken = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.1");
        var googleSameToken = CreateContext("/api/public/feeds/google/tok-a", "10.0.0.1");
        var tiktokSameToken = CreateContext("/api/public/feeds/tiktok/tok-a", "10.0.0.1");

        Assert.Equal(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(snapchatSame).PartitionKey);

        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(snapchatOtherIp).PartitionKey);

        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(snapchatOtherToken).PartitionKey);

        // Snapchat must not share a window with any other channel for the same
        // token/IP — abuse in one channel cannot mask or exhaust another.
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(metaSameToken).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(googleSameToken).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(snapchat).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(tiktokSameToken).PartitionKey);
    }

    // =========================================================================
    // Log redaction: snapchat feed token never reaches logs
    // =========================================================================

    [Fact]
    public void Redact_SnapchatFeedPath_ReplacesTokenWithFixedPlaceholder()
    {
        Assert.Equal(
            "/api/public/feeds/snapchat/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/snapchat/AbCd1234-Xy_z9"));
    }

    [Fact]
    public void Redact_SnapchatFeedPath_CaseInsensitive_AndExtraSegmentsCovered()
    {
        Assert.Equal(
            "/api/public/feeds/snapchat/<REDACTED>",
            PublicFeedPathRedactor.Redact("/API/PUBLIC/FEEDS/SNAPCHAT/abc123/extra"));
    }

    [Fact]
    public void Redact_MetaGoogleAndTikTokFeedPath_BehaviorUnchanged_BySnapchat()
    {
        Assert.Equal(
            "/api/public/feeds/meta/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/meta/abc123"));
        Assert.Equal(
            "/api/public/feeds/google/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/google/abc123"));
        Assert.Equal(
            "/api/public/feeds/tiktok/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/tiktok/abc123"));
        Assert.Equal("/api/public/products/42", PublicFeedPathRedactor.Redact("/api/public/products/42"));
    }

    [Fact]
    public void RedactEmbedded_SnapchatFeedTokenInsideRenderedText_IsRedacted()
    {
        var text = "RequestPath:/api/public/feeds/snapchat/AbCd1234-Xy_z9 RequestId:0H1";

        Assert.Equal(
            "RequestPath:/api/public/feeds/snapchat/<REDACTED> RequestId:0H1",
            PublicFeedPathRedactor.RedactEmbedded(text));
    }

    [Fact]
    public void RedactEmbedded_MixedAllChannelOccurrences_AllRedacted()
    {
        var text = "meta /api/public/feeds/meta/tokAAAA then google /api/public/feeds/google/tokBBBB then tiktok /api/public/feeds/tiktok/tokCCCC then snapchat /api/public/feeds/snapchat/tokDDDD end";

        var redacted = PublicFeedPathRedactor.RedactEmbedded(text);

        Assert.DoesNotContain("tokAAAA", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokBBBB", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokCCCC", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokDDDD", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/meta/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/google/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/tiktok/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/snapchat/<REDACTED>", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactEmbedded_SnapchatTokenNeverAppearsInOutput()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var text = $"GET /api/public/feeds/snapchat/{rawToken} 200";

        var redacted = PublicFeedPathRedactor.RedactEmbedded(text);

        Assert.DoesNotContain(rawToken, redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/snapchat/<REDACTED>", redacted, StringComparison.Ordinal);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static DefaultHttpContext CreateContext(string path, string? clientIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = clientIp == null ? null : IPAddress.Parse(clientIp);
        return context;
    }
}
