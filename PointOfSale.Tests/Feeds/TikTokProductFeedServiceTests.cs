using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-3: TikTok channel adapter contract — strict channel-scoped token
/// authorization (cross-channel tokens never authorize), shared projection
/// reuse, channel-isolated cache keys (Meta/Google/TikTok never collide),
/// validation-before-cache ordering, and token lifecycle on the tiktok channel.
/// </summary>
public sealed class TikTokProductFeedServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private string _metaRawToken = null!;
    private string _googleRawToken = null!;
    private string _tiktokRawToken = null!;
    private ProductFeedToken _tiktokToken = null!;
    private InMemoryMetaFeedCache _cache = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "TikTok Feed Store",
            Slug = "tiktokfeed",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _metaRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _metaRawToken));

        _googleRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.GoogleChannel, _googleRawToken));

        _tiktokRawToken = ProductFeedTokenService.GenerateRawToken();
        _tiktokToken = ProductFeedToken.Create(_tenant.Id, ProductFeedToken.TikTokChannel, _tiktokRawToken);
        _db.ProductFeedTokens.Add(_tiktokToken);

        await AddEligibleProductAsync("TikTok Product", 99m);
        await _db.SaveChangesAsync();

        _cache = new InMemoryMetaFeedCache();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private StorefrontUrlResolver CreateUrlResolver() => new(
        new ConfigurationBuilder().Build(),
        NullLogger<StorefrontUrlResolver>.Instance,
        null!);

    /// <summary>The certified Feed-1A service doubles as the shared projection.</summary>
    private MetaProductFeedService CreateProjection(IMetaFeedCache? cache = null) => new(
        _db,
        new MetaFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<MetaProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    private TikTokProductFeedService CreateService(IMetaFeedCache? cache = null) => new(
        _db,
        CreateProjection(cache),
        new TikTokFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<TikTokProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    private GoogleProductFeedService CreateGoogleService(IMetaFeedCache? cache = null) => new(
        _db,
        CreateProjection(cache),
        new GoogleFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<GoogleProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    // =========================================================================
    // Happy path: tiktok token -> TikTok RSS via the shared projection
    // =========================================================================

    [Fact]
    public async Task ValidTikTokToken_ReturnsTikTokRss_FromTheSharedProjection()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
        // TikTok uses bare element names — no g: namespace.
        Assert.DoesNotContain("xmlns:g", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Products feed for TikTok Feed Store - TikTok", result.Xml, StringComparison.Ordinal);
        Assert.Contains("TikTok Product", result.Xml, StringComparison.Ordinal);
        Assert.Equal(1, result.ItemCount);
    }

    [Fact]
    public async Task TikTokFeeds_ContainTheSameProductTruth_AsMetaAndGoogle()
    {
        var tiktokService = CreateService();
        var metaService = CreateProjection();
        var googleService = CreateGoogleService();

        var tiktok = await tiktokService.GenerateAsync(_tiktokRawToken);
        var meta = await metaService.GenerateAsync(_metaRawToken);
        var google = await googleService.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, tiktok.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, meta.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, google.Outcome);

        // Identical projection semantics: same item count, same product.
        Assert.Equal(meta.ItemCount, tiktok.ItemCount);
        Assert.Equal(google.ItemCount, tiktok.ItemCount);

        // All contain the same canonical product title.
        Assert.Contains("TikTok Product", tiktok.Xml, StringComparison.Ordinal);
        Assert.Contains("TikTok Product", meta.Xml, StringComparison.Ordinal);
        Assert.Contains("TikTok Product", google.Xml, StringComparison.Ordinal);

        // TikTok vocabulary: bare sku_id instead of g:id, no g: namespace.
        Assert.Contains("<sku_id>", tiktok.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<g:id>", tiktok.Xml, StringComparison.Ordinal);

        // Meta uses g:id with the g: namespace.
        Assert.Contains("<g:id>", meta.Xml, StringComparison.Ordinal);
        Assert.Contains("<g:id>", google.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TikTokFeed_EmitsRequiredSkuId_Availability_Condition_Price_Brand()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_tiktokRawToken);

        var xml = result.Xml!;
        // Required TikTok fields (official spec):
        Assert.Contains("<sku_id>", xml, StringComparison.Ordinal);
        Assert.Contains("<title>", xml, StringComparison.Ordinal);
        Assert.Contains("<link>", xml, StringComparison.Ordinal);
        Assert.Contains("<description>", xml, StringComparison.Ordinal);
        Assert.Contains("<price>", xml, StringComparison.Ordinal);
        Assert.Contains("<availability>", xml, StringComparison.Ordinal);
        Assert.Contains("<condition>", xml, StringComparison.Ordinal);
        Assert.Contains("<brand>", xml, StringComparison.Ordinal);
        Assert.Contains("<image_link>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TikTokReadiness_MissingBrand_IsExcludedWithStructuredCount()
    {
        await AddEligibleProductAsync("TikTok Missing Brand", 50m, product =>
        {
            product.Brand = null;
            product.Manufacturer = null;
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, result.ExcludedItemCount);
        Assert.Contains("TikTok Product", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("TikTok Missing Brand", result.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Channel-scoped authorization: cross-channel tokens NEVER authorize
    // =========================================================================

    [Fact]
    public async Task MetaToken_DoesNotAuthorizeTheTikTokRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_metaRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task GoogleToken_DoesNotAuthorizeTheTikTokRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task TikTokToken_DoesNotAuthorizeTheMetaRoute()
    {
        var metaService = CreateProjection();

        var result = await metaService.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
    }

    [Fact]
    public async Task TikTokToken_DoesNotAuthorizeTheGoogleRoute()
    {
        var googleService = CreateGoogleService();

        var result = await googleService.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
    }

    [Fact]
    public async Task UnknownToken_Returns404Equivalent_AndNeverTouchesTheCache()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(ProductFeedTokenService.GenerateRawToken());

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Equal(0, _cache.GetCalls);
        Assert.Equal(0, _cache.SetCalls);
    }

    [Fact]
    public async Task RevokedTikTokToken_Returns404Equivalent()
    {
        var service = CreateService();
        _tiktokToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
    }

    [Fact]
    public async Task InactiveTenant_ReturnsTenantInactive()
    {
        var service = CreateService();
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TenantInactive, result.Outcome);
    }

    // =========================================================================
    // Cache: channel-isolated keys, warm path, revocation beats cache
    // =========================================================================

    [Fact]
    public async Task CacheMiss_GeneratesAndCaches_OnTheTikTokChannelKey()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.False(result.CacheHit);
        Assert.Equal(1, _cache.SetCalls);
        var key = _cache.SetKeys.Single();
        Assert.Contains($"tenant:{_tenant.Id:N}:feed:tiktok:{_tiktokToken.Id:N}", key, StringComparison.Ordinal);
        Assert.DoesNotContain(_tiktokRawToken, key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaGoogleAndTikTokCacheKeys_NeverCollide()
    {
        var tiktokService = CreateService();
        var metaService = CreateProjection();
        var googleService = CreateGoogleService();

        await metaService.GenerateAsync(_metaRawToken);
        await googleService.GenerateAsync(_googleRawToken);
        await tiktokService.GenerateAsync(_tiktokRawToken);

        Assert.Equal(3, _cache.SetKeys.Distinct().Count());
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:meta:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:google:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:tiktok:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondRequest_ServedFromTikTokCache_WithoutReSerialization()
    {
        var service = CreateService();

        var cold = await service.GenerateAsync(_tiktokRawToken);
        var warm = await service.GenerateAsync(_tiktokRawToken);

        Assert.False(cold.CacheHit);
        Assert.True(warm.CacheHit);
        Assert.Equal(cold.Xml, warm.Xml);
        Assert.Equal(1, _cache.SetCalls);
        Assert.Equal(2, _cache.GetCalls);
        Assert.Equal((int)_cache.GetTtl().TotalSeconds, warm.CacheMaxAgeSeconds);
    }

    [Fact]
    public async Task RevokedTikTokToken_IsNotServedFromCache()
    {
        var service = CreateService();
        var cold = await service.GenerateAsync(_tiktokRawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, cold.Outcome);
        Assert.True(_cache.Contains(_tenant.Id, _tiktokToken.Id, ProductFeedToken.TikTokChannel));

        _tiktokToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var afterRevoke = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, afterRevoke.Outcome);
        Assert.Null(afterRevoke.Xml);
    }

    [Fact]
    public async Task RedisOutage_TikTokFeedStillServedFromSql()
    {
        var redis = new Mock<IRedisJsonCacheService>(MockBehavior.Strict);
        redis
            .Setup(r => r.GetAsync<MetaFeedCachePayload>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis down"));
        redis
            .Setup(r => r.SetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetaFeedCachePayload>(),
                It.IsAny<TimeSpan>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis down"));

        var realCache = FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry());
        var service = CreateService(realCache);

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome); // never a 500 because of Redis
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Token lifecycle on the tiktok channel
    // =========================================================================

    [Fact]
    public async Task CreateToken_TikTokChannel_PersistsHashAndChannel()
    {
        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);

        var created = await tokenService.CreateTokenAsync(_tenant.Id, ProductFeedToken.TikTokChannel, CancellationToken.None);

        Assert.Equal(ProductFeedToken.TikTokChannel, created.Channel);
        var stored = await _db.ProductFeedTokens.SingleAsync(t => t.Id == created.TokenId);
        Assert.Equal(ProductFeedToken.TikTokChannel, stored.Channel);
        Assert.Equal(ProductFeedToken.HashToken(created.RawToken), stored.TokenHash);
    }

    [Fact]
    public async Task CreateToken_UnsupportedChannel_IsRejected()
    {
        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenService.CreateTokenAsync(_tenant.Id, "invalid_channel", CancellationToken.None));
    }

    [Fact]
    public async Task RotateTikTokToken_PreservesChannel_AndInvalidatesTikTokCacheKey()
    {
        var service = CreateService();
        await service.GenerateAsync(_tiktokRawToken);
        Assert.True(_cache.Contains(_tenant.Id, _tiktokToken.Id, ProductFeedToken.TikTokChannel));

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var rotated = await tokenService.RotateTokenAsync(_tenant.Id, _tiktokToken.Id, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.Equal(ProductFeedToken.TikTokChannel, rotated!.Channel); // channel preserved
        Assert.False(_cache.Contains(_tenant.Id, _tiktokToken.Id, ProductFeedToken.TikTokChannel));
        Assert.Contains(
            _cache.InvalidatedKeys,
            key => key.Contains(":feed:tiktok:", StringComparison.Ordinal));

        var newFeed = await service.GenerateAsync(rotated.RawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, newFeed.Outcome);
        Assert.False(newFeed.CacheHit);
    }

    [Fact]
    public async Task RevokeTikTokToken_InvalidatesOnlyTheTikTokCacheKey_MetaEntryUntouched()
    {
        var tiktokService = CreateService();
        var metaService = CreateProjection();
        await metaService.GenerateAsync(_metaRawToken);
        await tiktokService.GenerateAsync(_tiktokRawToken);

        var metaToken = await _db.ProductFeedTokens.SingleAsync(t => t.Channel == ProductFeedToken.MetaChannel);

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var revoked = await tokenService.RevokeTokenAsync(_tenant.Id, _tiktokToken.Id, CancellationToken.None);

        Assert.True(revoked);
        Assert.False(_cache.Contains(_tenant.Id, _tiktokToken.Id, ProductFeedToken.TikTokChannel));
        Assert.True(_cache.Contains(_tenant.Id, metaToken.Id, ProductFeedToken.MetaChannel)); // untouched
    }

    // =========================================================================
    // Seeding helper (mirrors MetaFeedCachingTests conventions)
    // =========================================================================

    private async Task AddEligibleProductAsync(string name, decimal unitPrice, Action<Product>? customize = null)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Description = $"{name} description",
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = false,
            Brand = "Acme"
        };
        customize?.Invoke(product);
        _db.Products.Add(product);

        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = "Piece"
        };
        _db.Units.Add(unit);
        _db.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unit.Id,
            SellingPrice = unitPrice,
            IsDefault = true,
            IsBaseUnit = true,
            Unit = unit
        });

        _db.ProductMedia.Add(new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            ProductId = product.Id,
            MediaType = "image",
            Url = $"https://cdn.example.com/{product.Id}.jpg",
            Position = 0,
            CreatedAtUtc = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }
}
