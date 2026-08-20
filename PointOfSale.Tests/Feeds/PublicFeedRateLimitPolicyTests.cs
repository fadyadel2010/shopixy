using System.Net;
using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Controllers;
using PointOfSale.API.Infrastructure.RateLimiting;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1B: Dedicated PublicFeedRateLimit policy — fixed window sized for
/// catalog fetchers, token+IP partitioning, hygiene of the partition key, and
/// the guarantee that the policy is wired to the public Meta feed endpoint
/// only (no collateral on any other endpoint).
/// </summary>
public sealed class PublicFeedRateLimitPolicyTests
{
    // =========================================================================
    // Policy shape
    // =========================================================================

    [Fact]
    public void PolicyShape_MatchesFeed1BContract()
    {
        Assert.Equal("PublicFeedRateLimit", PublicFeedRateLimitPolicy.PolicyName);
        Assert.Equal(30, PublicFeedRateLimitPolicy.PermitLimit);
        Assert.Equal(60, PublicFeedRateLimitPolicy.WindowSeconds);
    }

    [Fact]
    public void CreateOptions_FixedWindow_NoQueue_AutoReplenishing()
    {
        var options = PublicFeedRateLimitPolicy.CreateOptions();

        Assert.Equal(30, options.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Window);
        Assert.Equal(0, options.QueueLimit);           // bursts reject, never queued
        Assert.True(options.AutoReplenishment);
        Assert.Equal(QueueProcessingOrder.OldestFirst, options.QueueProcessingOrder);
    }

    [Fact]
    public void CreateOptions_StillRejectsAtTheConfiguredThreshold()
    {
        using var limiter = new FixedWindowRateLimiter(PublicFeedRateLimitPolicy.CreateOptions());

        for (var i = 0; i < PublicFeedRateLimitPolicy.PermitLimit; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired);
        }

        using var rejected = limiter.AttemptAcquire(1);
        Assert.False(rejected.IsAcquired);
    }

    // =========================================================================
    // Partition keying
    // =========================================================================

    [Fact]
    public void PartitionKey_SeparatesTokensAndCallerIps()
    {
        var sameTokenSameIp = PublicFeedRateLimitPolicy.BuildPartitionKey("tok-a", "10.0.0.1");

        Assert.Equal(sameTokenSameIp, PublicFeedRateLimitPolicy.BuildPartitionKey("tok-a", "10.0.0.1"));
        Assert.NotEqual(sameTokenSameIp, PublicFeedRateLimitPolicy.BuildPartitionKey("tok-b", "10.0.0.1"));
        Assert.NotEqual(sameTokenSameIp, PublicFeedRateLimitPolicy.BuildPartitionKey("tok-a", "10.0.0.2"));
    }

    [Fact]
    public void PartitionKey_UsesTokenFingerprintAndNeverContainsRawToken()
    {
        const string RawToken = "abc-sensitive-feed-token";

        var partitionKey = PublicFeedRateLimitPolicy.BuildPartitionKey("meta", RawToken, "10.0.0.1");

        Assert.DoesNotContain(RawToken, partitionKey, StringComparison.Ordinal);
        Assert.Contains(PublicFeedRateLimitPolicy.FingerprintToken(RawToken), partitionKey, StringComparison.Ordinal);
        Assert.StartsWith("public-feed:meta:", partitionKey, StringComparison.Ordinal);
        Assert.EndsWith(":ip:10.0.0.1", partitionKey, StringComparison.Ordinal);
    }

    [Fact]
    public void PartitionKey_PreservesChannelTokenAndIpIsolation()
    {
        var baseline = PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.1");

        Assert.Equal(baseline, PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.1"));
        Assert.NotEqual(baseline, PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-b", "10.0.0.1"));
        Assert.NotEqual(baseline, PublicFeedRateLimitPolicy.BuildPartitionKey("google", "tok-a", "10.0.0.1"));
        Assert.NotEqual(baseline, PublicFeedRateLimitPolicy.BuildPartitionKey("meta", "tok-a", "10.0.0.2"));
    }

    [Fact]
    public void GetPartition_PartitionsByTokenAndClientIp()
    {
        var contextA = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.1");
        var contextSame = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.1");
        var contextOtherIp = CreateContext("/api/public/feeds/meta/tok-a", "10.0.0.2");
        var contextOtherToken = CreateContext("/api/public/feeds/meta/tok-b", "10.0.0.1");

        Assert.Equal(
            PublicFeedRateLimitPolicy.GetPartition(contextA).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(contextSame).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(contextA).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(contextOtherIp).PartitionKey);
        Assert.NotEqual(
            PublicFeedRateLimitPolicy.GetPartition(contextA).PartitionKey,
            PublicFeedRateLimitPolicy.GetPartition(contextOtherToken).PartitionKey);
    }

    [Fact]
    public void GetPartition_WithoutClientIp_FallsBackToConnectionIdentity()
    {
        var context = CreateContext("/api/public/feeds/meta/tok-a", clientIp: null);
        context.Connection.Id = "conn-42";

        var partitionKey = PublicFeedRateLimitPolicy.GetPartition(context).PartitionKey;

        Assert.Contains("conn-42", partitionKey, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-a", partitionKey, StringComparison.Ordinal);
        Assert.Contains(PublicFeedRateLimitPolicy.FingerprintToken("tok-a"), partitionKey, StringComparison.Ordinal);
    }

    // =========================================================================
    // Token extraction hygiene
    // =========================================================================

    [Theory]
    [InlineData("/api/public/feeds/meta/abc123", "abc123")]
    [InlineData("/api/public/feeds/meta/abc123/", "abc123")]
    [InlineData("/api/public/feeds/meta/ abc123 ", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/META/abc123", "abc123")]
    [InlineData("/api/public/feeds/snapchat/abc123", "abc123")]
    [InlineData("/API/PUBLIC/FEEDS/SNAPCHAT/abc123", "abc123")]
    public void ExtractToken_ReadsTheLastPathSegment(string path, string expected)
    {
        Assert.Equal(expected, PublicFeedRateLimitPolicy.ExtractToken(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/api/public/products")]
    [InlineData("/api/public/feeds/meta")]
    [InlineData("/api/public/feeds/meta/")]
    [InlineData("/api/public/feeds/meta/   ")]
    public void ExtractToken_MissingOrEmptyToken_CollapsesToSharedUnknownPartition(string? path)
    {
        Assert.Equal("unknown", PublicFeedRateLimitPolicy.ExtractToken(path));
    }

    // =========================================================================
    // Endpoint wiring: the policy must apply ONLY to the public feed endpoints
    // (Meta + Google channel adapters — Feed-2), and to nothing else.
    // =========================================================================

    [Fact]
    public void PublicFeedRateLimit_IsAppliedOnlyToThePublicFeedEndpoints()
    {
        var assembly = typeof(PublicMetaProductFeedController).Assembly;

        var endpoints = assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(method => method
                .GetCustomAttributes<EnableRateLimitingAttribute>()
                .Select(attribute => (Method: method, Policy: attribute.PolicyName)))
            .Where(entry => entry.Policy == PublicFeedRateLimitPolicy.PolicyName)
            .ToList();

        Assert.Equal(4, endpoints.Count);
        var wiring = endpoints
            .Select(entry => (entry.Method.DeclaringType, entry.Method.Name))
            .OrderBy(entry => entry.DeclaringType!.Name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                (DeclaringType: (Type?)typeof(PublicGoogleProductFeedController), Name: "GetFeed"),
                (DeclaringType: (Type?)typeof(PublicMetaProductFeedController), Name: "GetFeed"),
                (DeclaringType: (Type?)typeof(PublicSnapchatProductFeedController), Name: "GetFeed"),
                (DeclaringType: (Type?)typeof(PublicTikTokProductFeedController), Name: "GetFeed")
            },
            wiring);
    }

    [Fact]
    public void GetFeed_DeclaresThe429Contract()
    {
        var method = typeof(PublicMetaProductFeedController).GetMethod(nameof(PublicMetaProductFeedController.GetFeed))!;
        var attribute = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PublicFeedRateLimitPolicy.PolicyName, attribute!.PolicyName);
        Assert.Contains(
            method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>(),
            produced => produced.StatusCode == StatusCodes.Status429TooManyRequests);
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
