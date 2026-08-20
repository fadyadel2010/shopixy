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
/// FEED-4: Public anonymous Snapchat feed endpoint contract — token-driven
/// tenant resolution, indistinguishable 404s, RSS content type with g: namespace,
/// Cache-Control parity, JWT/header independence, and the hard guarantee that
/// Meta, Google, and TikTok tokens never authorize the Snapchat route.
/// </summary>
public sealed class PublicSnapchatFeedControllerTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PublicSnapchatProductFeedController _controller = null!;
    private Tenant _tenant = null!;
    private string _snapchatRawToken = null!;
    private string _metaRawToken = null!;
    private string _googleRawToken = null!;
    private string _tiktokRawToken = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Snapchat Public Store",
            Slug = "snapchatpublic",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _snapchatRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.SnapchatChannel, _snapchatRawToken));

        _metaRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _metaRawToken));

        _googleRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.GoogleChannel, _googleRawToken));

        _tiktokRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.TikTokChannel, _tiktokRawToken));

        await _db.SaveChangesAsync();

        var urlResolver = new StorefrontUrlResolver(
            new ConfigurationBuilder().Build(),
            NullLogger<StorefrontUrlResolver>.Instance,
            null!);
        var cache = new InMemoryMetaFeedCache();
        var projection = new MetaProductFeedService(
            _db,
            new MetaFeedXmlSerializer(),
            urlResolver,
            NullLogger<MetaProductFeedService>.Instance,
            cache,
            FeedTestFactory.CreateTelemetry());
        var feedService = new SnapchatProductFeedService(
            _db,
            projection,
            new SnapchatFeedXmlSerializer(),
            urlResolver,
            NullLogger<SnapchatProductFeedService>.Instance,
            cache,
            FeedTestFactory.CreateTelemetry());

        _controller = new PublicSnapchatProductFeedController(
            feedService,
            NullLogger<PublicSnapchatProductFeedController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ValidSnapchatToken_Returns200_WithRssContentType()
    {
        var result = await _controller.GetFeed(_snapchatRawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.Equal("application/rss+xml; charset=utf-8", content.ContentType);
        Assert.Contains("<rss", content.Content, StringComparison.Ordinal);
        Assert.Contains("xmlns:g=\"http://base.google.com/ns/1.0\"", content.Content, StringComparison.Ordinal);
        Assert.Contains("Snapchat Public Store", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaToken_OnTheSnapchatRoute_Returns404()
    {
        var result = await _controller.GetFeed(_metaRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GoogleToken_OnTheSnapchatRoute_Returns404()
    {
        var result = await _controller.GetFeed(_googleRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TikTokToken_OnTheSnapchatRoute_Returns404()
    {
        var result = await _controller.GetFeed(_tiktokRawToken);

        Assert.IsType<NotFoundResult>(result);
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
        var token = await _db.ProductFeedTokens.SingleAsync(t => t.Channel == ProductFeedToken.SnapchatChannel);
        token.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_snapchatRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task InactiveTenant_Returns404_IndistinguishableFromKnown()
    {
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_snapchatRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EmptyCatalog_Returns200_WithZeroItems()
    {
        var result = await _controller.GetFeed(_snapchatRawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.DoesNotContain("<item>", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Success_SetsPublicCacheControl_MirroringTheCacheTtl()
    {
        var result = await _controller.GetFeed(_snapchatRawToken);

        Assert.IsType<ContentResult>(result);
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
    public void EndpointRequires_NoJwtAndNoTenantContextDependency()
    {
        var controllerType = typeof(PublicSnapchatProductFeedController);

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
    public void EndpointRoute_IsThePublicSnapchatFeedPath()
    {
        var route = typeof(PublicSnapchatProductFeedController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>()
            .Single();

        Assert.Equal("api/public/feeds/snapchat", route.Template);
    }

    [Fact]
    public async Task TokenWhitespacePadding_IsNormalized()
    {
        var result = await _controller.GetFeed($"  {_snapchatRawToken}  ");

        Assert.IsType<ContentResult>(result);
    }
}
