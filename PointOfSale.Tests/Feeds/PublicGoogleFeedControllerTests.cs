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
/// FEED-2: Public anonymous Google feed endpoint contract — token-driven
/// tenant resolution, indistinguishable 404s, RSS content type, Cache-Control
/// parity, JWT/header independence, and the hard guarantee that a Meta token
/// never authorizes the Google route.
/// </summary>
public sealed class PublicGoogleFeedControllerTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PublicGoogleProductFeedController _controller = null!;
    private Tenant _tenant = null!;
    private string _googleRawToken = null!;
    private string _metaRawToken = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Google Public Store",
            Slug = "googlepublic",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _googleRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.GoogleChannel, _googleRawToken));

        _metaRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _metaRawToken));

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
        var feedService = new GoogleProductFeedService(
            _db,
            projection,
            new GoogleFeedXmlSerializer(),
            urlResolver,
            NullLogger<GoogleProductFeedService>.Instance,
            cache,
            FeedTestFactory.CreateTelemetry());

        _controller = new PublicGoogleProductFeedController(
            feedService,
            NullLogger<PublicGoogleProductFeedController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ValidGoogleToken_Returns200_WithRssContentType()
    {
        var result = await _controller.GetFeed(_googleRawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.Equal("application/rss+xml; charset=utf-8", content.ContentType);
        Assert.Contains("<rss", content.Content, StringComparison.Ordinal);
        Assert.Contains("xmlns:g=\"http://base.google.com/ns/1.0\"", content.Content, StringComparison.Ordinal);
        Assert.Contains("Google Public Store", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaToken_OnTheGoogleRoute_Returns404()
    {
        var result = await _controller.GetFeed(_metaRawToken);

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
        var token = await _db.ProductFeedTokens.SingleAsync(t => t.Channel == ProductFeedToken.GoogleChannel);
        token.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_googleRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task InactiveTenant_Returns404_IndistinguishableFromUnknown()
    {
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _controller.GetFeed(_googleRawToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EmptyCatalog_Returns200_WithZeroItems()
    {
        var result = await _controller.GetFeed(_googleRawToken);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.DoesNotContain("<item>", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Success_SetsPublicCacheControl_MirroringTheCacheTtl()
    {
        var result = await _controller.GetFeed(_googleRawToken);

        Assert.IsType<ContentResult>(result);
        Assert.Equal(
            "public, max-age=300",
            _controller.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void EndpointRequires_NoJwtAndNoTenantContextDependency()
    {
        var controllerType = typeof(PublicGoogleProductFeedController);

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
    public void EndpointRoute_IsThePublicGoogleFeedPath()
    {
        var route = typeof(PublicGoogleProductFeedController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>()
            .Single();

        Assert.Equal("api/public/feeds/google", route.Template);
    }

    [Fact]
    public async Task TokenWhitespacePadding_IsNormalized()
    {
        var result = await _controller.GetFeed($"  {_googleRawToken}  ");

        Assert.IsType<ContentResult>(result);
    }
}
