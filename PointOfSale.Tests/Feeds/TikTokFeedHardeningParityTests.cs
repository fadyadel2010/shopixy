using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Middleware;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-3 hardening parity: the shared Feed-1B safeguards extend to the TikTok
/// channel without weakening Meta or Google — rate-limit channel extraction /
/// partition independence, and token redaction of the tiktok public feed path in
/// logs.
/// </summary>
public sealed class TikTokFeedHardeningParityTests
{
    // =========================================================================
    // Rate limiting: channel-aware partitioning
    // =========================================================================

    [Theory]
    [InlineData("/api/public/feeds/meta/tok-a", "meta")]
    [InlineData("/api/public/feeds/google/tok-a", "google")]
    [InlineData("/api/public/feeds/tiktok/tok-a", "tiktok")]
    [InlineData("/API/PUBLIC/FEEDS/TIKTOK/tok-a", "tiktok")]
    [InlineData("/api/public/feeds/snapchat/tok-a", "snapchat")]
    [InlineData("/API/PUBLIC/FEEDS/SNAPCHAT/tok-a", "snapchat")]
    [InlineData("/api/public/products", "unknown")]
    [InlineData(null, "unknown")]
    public void ExtractChannel_ReadsTheTikTokChannelSegment(string? path, string expected)
    {
        Assert.Equal(expected, PublicFeedRateLimitPolicy.ExtractChannel(path));
    }

    [Theory]
    [InlineData("/api/public/feeds/tiktok/abc123", "abc123")]
    [InlineData("/api/public/feeds/tiktok/abc123/", "abc123")]
    [InlineData("/api/public/feeds/tiktok/ abc123 ", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/TIKTOK/abc123", "abc123")]
    [InlineData("/api/public/feeds/snapchat/abc123", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/SNAPCHAT/abc123", "abc123")]
    public void ExtractToken_ReadsTheLastPathSegment_OnTheTikTokRoute(string path, string expected)
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

    [Fact]
    public void MetaGoogleAndTikTokPartitions_AreIndependent_SameTokenNeverSharesAWindow()
    {
        var metaKey = PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.1");
        var googleKey = PublicFeedRateLimitPolicy.BuildPartitionKey("google", "tok-a", "10.0.0.1");
        var tiktokKey = PublicFeedRateLimitPolicy.BuildPartitionKey("tiktok", "tok-a", "10.0.0.1");
        var fingerprint = PublicFeedRateLimitPolicy.FingerprintToken("tok-a");

        Assert.Equal($"public-feed:meta:{fingerprint}:ip:10.0.0.1", metaKey);
        Assert.Equal($"public-feed:google:{fingerprint}:ip:10.0.0.1", googleKey);
        Assert.Equal($"public-feed:tiktok:{fingerprint}:ip:10.0.0.1", tiktokKey);
        Assert.NotEqual(metaKey, googleKey);
        Assert.NotEqual(metaKey, tiktokKey);
        Assert.NotEqual(googleKey, tiktokKey);
    }

    [Fact]
    public void GetPartition_TikTokRoute_PartitionsByChannelTokenAndIp()
    {
        var tiktok = CreateContext("/api/public/feeds/tiktok/tok-a", "10.0.0.1");
        var tiktokSame = CreateContext("/api/public/feeds/tiktok/tok-a", "10.0.0.1");
        var tiktokOtherIp = CreateContext("/api/public/feeds/tiktok/tok-a", "10.0.0.2");
        var tiktokOtherToken = CreateContext("/api/public/feeds/tiktok/tok-b", "10.0.0.1");
        var metaSameToken = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.1");
        var googleSameToken = CreateContext("/api/public/feeds/google/tok-a", "10.0.0.1");

        Assert.Equal(
            PublicFeedRateLimitPolicy.GetPartition(tiktok).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(tiktokSame).PartitionKey);

        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(tiktok).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(tiktokOtherIp).PartitionKey);

        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(tiktok).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(tiktokOtherToken).PartitionKey);

        // The Meta and Google windows for the same token/IP are different partitions.
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(tiktok).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(metaSameToken).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(tiktok).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(googleSameToken).PartitionKey);
    }

    // =========================================================================
    // Log redaction: tiktok feed token never reaches logs
    // =========================================================================

    [Fact]
    public void Redact_TikTokFeedPath_ReplacesTokenWithFixedPlaceholder()
    {
        Assert.Equal(
            "/api/public/feeds/tiktok/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/tiktok/AbCd1234-Xy_z9"));
    }

    [Fact]
    public void Redact_TikTokFeedPath_CaseInsensitive_AndExtraSegmentsCovered()
    {
        Assert.Equal(
            "/api/public/feeds/tiktok/<REDACTED>",
            PublicFeedPathRedactor.Redact("/API/PUBLIC/FEEDS/TIKTOK/abc123/extra"));
    }

    [Fact]
    public void Redact_MetaAndGoogleFeedPath_BehaviorUnchanged()
    {
        Assert.Equal(
            "/api/public/feeds/meta/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/meta/abc123"));
        Assert.Equal(
            "/api/public/feeds/google/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/google/abc123"));
        Assert.Equal("/api/public/products/42", PublicFeedPathRedactor.Redact("/api/public/products/42"));
    }

    [Fact]
    public void RedactEmbedded_TikTokFeedTokenInsideRenderedText_IsRedacted()
    {
        var text = "RequestPath:/api/public/feeds/tiktok/AbCd1234-Xy_z9 RequestId:0H1";

        Assert.Equal(
            "RequestPath:/api/public/feeds/tiktok/<REDACTED> RequestId:0H1",
            PublicFeedPathRedactor.RedactEmbedded(text));
    }

    [Fact]
    public void RedactEmbedded_MixedMetaGoogleAndTikTokOccurrences_AllRedacted()
    {
        var text = "meta /api/public/feeds/meta/tokAAAA then google /api/public/feeds/google/tokBBBB then tiktok /api/public/feeds/tiktok/tokCCCC end";

        var redacted = PublicFeedPathRedactor.RedactEmbedded(text);

        Assert.DoesNotContain("tokAAAA", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokBBBB", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokCCCC", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/meta/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/google/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/tiktok/<REDACTED>", redacted, StringComparison.Ordinal);
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
