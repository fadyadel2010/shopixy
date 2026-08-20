using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1A: Public anonymous feed endpoint contract — token-driven tenant
/// resolution, indistinguishable 404s, RSS content type, empty-catalog 200,
/// and independence from JWT/tenant headers.
/// </summary>
public sealed class PublicMetaFeedControllerTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PublicMetaProductFeedController _controller = null!;
    private Tenant _tenant = null!;
    private string _rawToken = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Public Feed Store",
            Slug = "publicfeed",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _rawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _rawToken));
        await _db.SaveChangesAsync();

        var urlResolver = new StorefrontUrlResolver(
            new ConfigurationBuilder().Build(),
            NullLogger<StorefrontUrlResolver>.Instance,
            null!);
        var feedService = new MetaProductFeedService(
            _db,
            new MetaFeedXmlSerializer(),
            urlResolver,
            NullLogger<MetaProductFeedService>.Instance,
            new InMemoryMetaFeedCache(),
            FeedTestFactory.CreateTelemetry());

        _controller = new PublicMetaProductFeedController(
            feedService,
            NullLogger<PublicMetaProductFeedController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ValidToken_Returns200_WithRssContentType()
    {
        var result = await _controller.GetFeed(_rawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.Equal("application/rss+xml; charset=utf-8", content.ContentType);
        Assert.Contains("<rss", content.Content, StringComparison.Ordinal);
        Assert.Contains("Public Feed Store", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyCatalog_Returns200_WithZeroItems()
    {
        var result = await _controller.GetFeed(_rawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.DoesNotContain("<item>", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownToken_Returns404_WithNoBody()
    {
        var result = await _controller.GetFeed(ProductFeedTokenService.GenerateRawToken());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RevokedToken_Returns404_IndistinguishableFromUnknown()
    {
        var token = await _db.ProductFeedTokens.SingleAsync();
        token.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_rawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task InactiveTenant_Returns404_IndistinguishableFromUnknown()
    {
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_rawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TenantComesOnlyFromToken_SpoofedTenantContextCannotAlterIt()
    {
        // No tenant context is injected into the controller or the feed service;
        // a hostile ambient tenant (as X-Tenant-Id spoofing would produce) cannot
        // influence generation because nothing in the path reads it.
        var ambientTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Spoofed Store",
            Slug = "spoofed",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://spoofed.example.com"
        };
        _db.Tenants.Add(ambientTenant);
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_rawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Public Feed Store", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Spoofed Store", content.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("spoofed.example.com", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointRequires_NoJwtAndNoTenantContextDependency()
    {
        var controllerType = typeof(PublicMetaProductFeedController);

        Assert.Contains(
            controllerType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true),
            _ => true);
        Assert.DoesNotContain(
            controllerType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true),
            _ => true);
        Assert.DoesNotContain(
            controllerType.GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType.Name == "ITenantContext");
    }

    [Fact]
    public async Task Success_SetsPublicCacheControl_MirroringTheCacheTtl()
    {
        var result = await _controller.GetFeed(_rawToken);

        Assert.IsType<ContentResult>(result);
        // FEED-1B: client-side max-age mirrors the server-side rendered-XML TTL.
        Assert.Equal(
            "public, max-age=300",
            _controller.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task NotFound_SetsNoCacheControlHeader()
    {
        var result = await _controller.GetFeed(ProductFeedTokenService.GenerateRawToken());

        Assert.IsType<NotFoundResult>(result);
        Assert.True(_controller.HttpContext.Response.Headers.CacheControl.Count == 0);
    }

    [Fact]
    public async Task TokenWhitespacePadding_IsNormalized()
    {
        var result = await _controller.GetFeed($"  {_rawToken}  ");

        Assert.IsType<ContentResult>(result);
    }
}
