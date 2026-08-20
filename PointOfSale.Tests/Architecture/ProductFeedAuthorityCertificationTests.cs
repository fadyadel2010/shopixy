using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// FEED-1A authority certification: the public Meta feed path must stay
/// anonymous and token-driven, the admin token API must stay Owner/Admin-only,
/// tenant resolution must bypass public feed paths, the feed token entity must
/// never expose a raw token, and the feed path must never read tenant headers.
/// </summary>
public sealed class ProductFeedAuthorityCertificationTests
{
    [Fact]
    public void PublicFeedController_IsAnonymous_WithNoAuthorizeAttribute()
    {
        var controllerType = typeof(PublicMetaProductFeedController);

        Assert.Contains(
            controllerType.GetCustomAttributes(typeof(AllowAnonymousAttribute), true),
            _ => true);
        Assert.DoesNotContain(
            controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            _ => true);

        var getFeed = controllerType.GetMethod("GetFeed");
        Assert.NotNull(getFeed);
        Assert.DoesNotContain(
            getFeed!.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            _ => true);
    }

    [Fact]
    public void AdminTokenController_RequiresOwnerOrAdminRole()
    {
        var attribute = typeof(AdminProductFeedTokensController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("Owner,Admin", attribute.Roles);
    }

    [Fact]
    public void BothResolutionMiddlewares_ExcludePublicFeedPath()
    {
        var tenantMiddleware = Read(ApiPath("Middleware", "TenantResolutionMiddleware.cs"));
        Assert.Contains("\"/api/public/feeds\"", tenantMiddleware, StringComparison.Ordinal);

        var hostMiddleware = Read(ApiPath("Middleware", "HostResolutionMiddleware.cs"));
        Assert.Contains("\"/api/public/feeds\"", hostMiddleware, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedTokenEntity_NeverExposesRawToken()
    {
        var properties = typeof(ProductFeedToken)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToList();

        // Exactly the governance surface — any new property requires
        // recertification. Every declared property must be in the allowed set.
        var allowed = new HashSet<string> { "TenantId", "Channel", "TokenHash", "CreatedAtUtc", "RevokedAtUtc", "IsActive" };
        Assert.Subset(allowed, properties.ToHashSet());
        Assert.NotEmpty(properties);
        Assert.DoesNotContain(properties, name => name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name == "Token");

        // The hash is the only token material and is written only via factory.
        var tokenHash = typeof(ProductFeedToken).GetProperty("TokenHash")!;
        Assert.NotNull(tokenHash.GetMethod);
        Assert.True(tokenHash.SetMethod == null || tokenHash.SetMethod.IsPrivate);
    }

    [Fact]
    public void FeedPath_NeverReadsTenantHeaderOrAmbientTenantContext()
    {
        var publicController = Read(ApiPath("Controllers", "PublicMetaProductFeedController.cs"));
        var feedService = Read(ApiPath("Services", "Feeds", "MetaProductFeedService.cs"));
        var tokenService = Read(ApiPath("Services", "Feeds", "ProductFeedTokenService.cs"));

        foreach (var source in new[] { publicController, feedService, tokenService })
        {
            Assert.DoesNotContain("X-Tenant-Id", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TenantIdHeaderName", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ITenantContext", source, StringComparison.Ordinal);
        }

        // Tenant authority derives exclusively from the hashed token row.
        Assert.Contains("ProductFeedToken.HashToken", feedService, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", feedService, StringComparison.Ordinal);
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(
            Path.Combine(
                new[] { TestConfiguration.GetSolutionRoot(), "PointOfSale.API" }
                    .Concat(parts)
                    .ToArray()));
}
