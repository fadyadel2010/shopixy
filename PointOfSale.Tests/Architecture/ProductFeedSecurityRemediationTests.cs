using PointOfSale.API.Middleware;
using PointOfSale.Core.Entities;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductFeedSecurityRemediationTests
{
    [Fact]
    public void AdminTokenController_UsesRouteChannelForListRevokeAndRotate()
    {
        var source = Read(ApiPath("Controllers", "AdminProductFeedTokensController.cs"));

        Assert.Contains("ListTokensAsync(_tenantContext.RequiredTenantId, channel, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("RevokeTokenAsync(_tenantContext.RequiredTenantId, channel, tokenId, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("RotateTokenAsync(_tenantContext.RequiredTenantId, channel, tokenId, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductFeedTokenService_ChannelScopedOperationsIncludeChannelPredicate()
    {
        var source = Read(ApiPath("Services", "Feeds", "ProductFeedTokenService.cs"));

        Assert.Contains("token.TenantId == tenantId && token.Channel == channel && !token.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains("item.TenantId == tenantId && item.Channel == channel && !item.IsDeleted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_DoesNotWireNonRedactingDebugOrEventSourceProviders()
    {
        var source = Read(ApiPath("Program.cs"));

        Assert.DoesNotContain("AddDebug()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddEventSourceLogger()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void JwtEventPathLogging_UsesPublicFeedPathRedactor()
    {
        var program = Read(ApiPath("Program.cs"));
        var jwtConfiguration = Read(ApiPath("Configuration", "JwtMiddlewareConfiguration.cs"));

        Assert.Contains("PublicFeedPathRedactor.Redact(context.HttpContext.Request.Path.Value ?? \"unknown\")", program, StringComparison.Ordinal);
        Assert.Contains("PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? \"unknown\")", program, StringComparison.Ordinal);
        Assert.Contains("PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? \"unknown\")", jwtConfiguration, StringComparison.Ordinal);

        Assert.DoesNotContain("context.Request.Path);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Request.Path);", jwtConfiguration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProductFeedToken.MetaChannel)]
    [InlineData(ProductFeedToken.GoogleChannel)]
    [InlineData(ProductFeedToken.TikTokChannel)]
    [InlineData(ProductFeedToken.SnapchatChannel)]
    public void PublicFeedPathRedactor_RedactsEverySupportedChannel_AndPreservesOtherRoutes(string channel)
    {
        const string RawToken = "AbCdEf0123_-GhIjKl";
        var feedPath = $"/api/public/feeds/{channel}/{RawToken}";

        Assert.Equal($"/api/public/feeds/{channel}/<REDACTED>", PublicFeedPathRedactor.Redact(feedPath));
        Assert.DoesNotContain(RawToken, PublicFeedPathRedactor.RedactEmbedded($"GET {feedPath}"), StringComparison.Ordinal);
        Assert.Equal("/api/products", PublicFeedPathRedactor.Redact("/api/products"));
    }

    [Fact]
    public void RepositoryGuard_IgnoresAndRemovesTokenCheckArtifact()
    {
        var root = TestConfiguration.GetSolutionRoot();
        var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));

        Assert.Contains("token_check.json", gitignore, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "token_check.json")));
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(
            Path.Combine(
                new[] { TestConfiguration.GetSolutionRoot(), "PointOfSale.API" }
                    .Concat(parts)
                    .ToArray()));
}
