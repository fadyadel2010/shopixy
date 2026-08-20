using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Middleware;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-2 hardening parity: the shared Feed-1B safeguards extend to the Google
/// channel without weakening Meta — rate-limit channel extraction/partition
/// independence, and token redaction of the google public feed path in logs.
/// </summary>
public sealed class GoogleFeedHardeningParityTests
{
    // =========================================================================
    // Rate limiting: channel-aware partitioning
    // =========================================================================

    [Theory]
    [InlineData("/api/public/feeds/meta/tok-a", "meta")]
    [InlineData("/api/public/feeds/google/tok-a", "google")]
    [InlineData("/API/PUBLIC/FEEDS/GOOGLE/tok-a", "google")]
    [InlineData("/api/public/products", "unknown")]
    [InlineData(null, "unknown")]
    public void ExtractChannel_ReadsTheChannelSegment(string? path, string expected)
    {
        Assert.Equal(expected, PublicFeedRateLimitPolicy.ExtractChannel(path));
    }

    [Theory]
    [InlineData("/api/public/feeds/google/abc123", "abc123")]
    [InlineData("/api/public/feeds/google/abc123/", "abc123")]
    [InlineData("/api/public/feeds/google/ abc123 ", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/GOOGLE/abc123", "abc123")]
    public void ExtractToken_ReadsTheLastPathSegment_OnTheGoogleRoute(string path, string expected)
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
    public void MetaAndGooglePartitions_AreIndependent_SameTokenNeverSharesAWindow()
    {
        var metaKey = PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.1");
        var googleKey = PublicFeedRateLimitPolicy.BuildPartitionKey("google", "tok-a", "10.0.0.1");
        var fingerprint = PublicFeedRateLimitPolicy.FingerprintToken("tok-a");

        Assert.Equal($"public-feed:meta:{fingerprint}:ip:10.0.0.1", metaKey);
        Assert.Equal($"public-feed:google:{fingerprint}:ip:10.0.0.1", googleKey);
        Assert.NotEqual(metaKey, googleKey);
    }

    [Fact]
    public void GetPartition_GoogleRoute_PartitionsByChannelTokenAndIp()
    {
        var google = CreateContext("/api/public/feeds/google/tok-a", "10.0.0.1");
        var googleSame = CreateContext("/api/public/feeds/google/tok-a", "10.0.0.1");
        var googleOtherIp = CreateContext("/api/public/feeds/google/tok-a", "10.0.0.2");
        var metaSameToken = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.1");

        Assert.Equal(
            PublicFeedRateLimitPolicy.GetPartition(google).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(googleSame).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(google).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(googleOtherIp).PartitionKey);
        // The Meta window for the same token/IP is a different partition.
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(google).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(metaSameToken).PartitionKey);
    }

    // =========================================================================
    // Log redaction: google feed token never reaches logs
    // =========================================================================

    [Fact]
    public void Redact_GoogleFeedPath_ReplacesTokenWithFixedPlaceholder()
    {
        Assert.Equal(
            "/api/public/feeds/google/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/google/AbCd1234-Xy_z9"));
    }

    [Fact]
    public void Redact_GoogleFeedPath_CaseInsensitive_AndExtraSegmentsCovered()
    {
        Assert.Equal(
            "/api/public/feeds/google/<REDACTED>",
            PublicFeedPathRedactor.Redact("/API/PUBLIC/FEEDS/GOOGLE/abc123/extra"));
    }

    [Fact]
    public void Redact_MetaFeedPath_BehaviorUnchanged()
    {
        Assert.Equal(
            "/api/public/feeds/meta/<REDACTED>",
            PublicFeedPathRedactor.Redact("/api/public/feeds/meta/abc123"));
        Assert.Equal("/api/public/products/42", PublicFeedPathRedactor.Redact("/api/public/products/42"));
    }

    [Fact]
    public void RedactEmbedded_GoogleFeedTokenInsideRenderedText_IsRedacted()
    {
        var text = "RequestPath:/api/public/feeds/google/AbCd1234-Xy_z9 RequestId:0H1";

        Assert.Equal(
            "RequestPath:/api/public/feeds/google/<REDACTED> RequestId:0H1",
            PublicFeedPathRedactor.RedactEmbedded(text));
    }

    [Fact]
    public void RedactEmbedded_MixedMetaAndGoogleOccurrences_AllRedacted()
    {
        var text = "meta /api/public/feeds/meta/tokAAAA then google /api/public/feeds/google/tokBBBB end";

        var redacted = PublicFeedPathRedactor.RedactEmbedded(text);

        Assert.DoesNotContain("tokAAAA", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokBBBB", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/meta/<REDACTED>", redacted, StringComparison.Ordinal);
        Assert.Contains("/api/public/feeds/google/<REDACTED>", redacted, StringComparison.Ordinal);
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
