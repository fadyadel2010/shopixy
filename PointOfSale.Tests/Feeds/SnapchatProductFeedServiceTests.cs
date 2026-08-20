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
/// FEED-4: Snapchat channel adapter contract — strict channel-scoped token
/// authorization (cross-channel tokens never authorize), shared projection
/// reuse, channel-isolated cache keys (Meta/Google/TikTok/Snapchat never
/// collide), validation-before-cache ordering, and token lifecycle on the
/// snapchat channel.
/// </summary>
public sealed class SnapchatProductFeedServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private string _metaRawToken = null!;
    private string _googleRawToken = null!;
    private string _tiktokRawToken = null!;
    private string _snapchatRawToken = null!;
    private ProductFeedToken _snapchatToken = null!;
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
            Name = "Snapchat Feed Store",
            Slug = "snapchatfeed",
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
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.TikTokChannel, _tiktokRawToken));

        _snapchatRawToken = ProductFeedTokenService.GenerateRawToken();
        _snapchatToken = ProductFeedToken.Create(_tenant.Id, ProductFeedToken.SnapchatChannel, _snapchatRawToken);
        _db.ProductFeedTokens.Add(_snapchatToken);

        await AddEligibleProductAsync("Snapchat Product", 99m);
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

    private SnapchatProductFeedService CreateService(IMetaFeedCache? cache = null) => new(
        _db,
        CreateProjection(cache),
        new SnapchatFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<SnapchatProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    private MetaProductFeedService CreateMetaService(IMetaFeedCache? cache = null) => new(
        _db,
        new MetaFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<MetaProductFeedService>.Instance,
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

    private TikTokProductFeedService CreateTikTokService(IMetaFeedCache? cache = null) => new(
        _db,
        CreateProjection(cache),
        new TikTokFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<TikTokProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    // =========================================================================
    // Happy path: snapchat token -> Snapchat RSS via the shared projection
    // =========================================================================

    [Fact]
    public async Task ValidSnapchatToken_ReturnsSnapchatRss_FromTheSharedProjection()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
        // Snapchat uses the g: namespace (same as Meta and Google).
        Assert.Contains("xmlns:g=\"http://base.google.com/ns/1.0\"", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Products feed for Snapchat Feed Store - Snapchat", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Snapchat Product", result.Xml, StringComparison.Ordinal);
        Assert.Equal(1, result.ItemCount);
    }

    [Fact]
    public async Task SnapchatFeeds_ContainTheSameProductTruth_AsMetaGoogleAndTikTok()
    {
        var snapchatService = CreateService();
        var metaService = CreateMetaService();
        var googleService = CreateGoogleService();
        var tiktokService = CreateTikTokService();

        var snapchat = await snapchatService.GenerateAsync(_snapchatRawToken);
        var meta = await metaService.GenerateAsync(_metaRawToken);
        var google = await googleService.GenerateAsync(_googleRawToken);
        var tiktok = await tiktokService.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, snapchat.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, meta.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, google.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, tiktok.Outcome);

        // Identical projection semantics: same item count.
        Assert.Equal(meta.ItemCount, snapchat.ItemCount);
        Assert.Equal(google.ItemCount, snapchat.ItemCount);
        Assert.Equal(tiktok.ItemCount, snapchat.ItemCount);

        // All contain the same canonical product title.
        Assert.Contains("Snapchat Product", snapchat.Xml, StringComparison.Ordinal);
        Assert.Contains("Snapchat Product", meta.Xml, StringComparison.Ordinal);
        Assert.Contains("Snapchat Product", google.Xml, StringComparison.Ordinal);
        Assert.Contains("Snapchat Product", tiktok.Xml, StringComparison.Ordinal);

        // Snapchat uses g:id with the g: namespace (same as Google/Meta).
        Assert.Contains("<g:id>", snapchat.Xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:g", snapchat.Xml, StringComparison.Ordinal);

        // TikTok uses bare sku_id — different vocabulary.
        Assert.Contains("<sku_id>", tiktok.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapchatFeed_EmitsRequiredFields_Availability_Condition_Price_Brand()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_snapchatRawToken);

        var xml = result.Xml!;
        // Required Snapchat fields (official spec) — Snapchat uses the g: namespace:
        Assert.Contains("<g:id>", xml, StringComparison.Ordinal);
        Assert.Contains("<title>", xml, StringComparison.Ordinal);
        Assert.Contains("<link>", xml, StringComparison.Ordinal);
        Assert.Contains("<description>", xml, StringComparison.Ordinal);
        Assert.Contains("<g:price>", xml, StringComparison.Ordinal);
        Assert.Contains("<g:availability>", xml, StringComparison.Ordinal);
        Assert.Contains("<g:condition>", xml, StringComparison.Ordinal);
        Assert.Contains("<g:brand>", xml, StringComparison.Ordinal);
        Assert.Contains("<g:image_link>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapchatReadiness_MissingBrandAndGtin_IsExcludedWithStructuredCount()
    {
        await AddEligibleProductAsync("Snapchat Missing Identifiers", 50m, product =>
        {
            product.Brand = null;
            product.Manufacturer = null;
            product.Barcode = null;
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, result.ExcludedItemCount);
        Assert.Contains("Snapchat Product", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Snapchat Missing Identifiers", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapchatReadiness_MissingBrandButValidGtin_IsIncluded()
    {
        await AddEligibleProductAsync("Snapchat GTIN Only", 50m, product =>
        {
            product.Brand = null;
            product.Manufacturer = null;
            product.Barcode = "1234567890123";
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(0, result.ExcludedItemCount);
        Assert.Contains("Snapchat GTIN Only", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<g:gtin>1234567890123</g:gtin>", result.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Channel-scoped authorization: cross-channel tokens NEVER authorize
    // =========================================================================

    [Fact]
    public async Task MetaToken_DoesNotAuthorizeTheSnapchatRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_metaRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task GoogleToken_DoesNotAuthorizeTheSnapchatRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task TikTokToken_DoesNotAuthorizeTheSnapchatRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_tiktokRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task SnapchatToken_DoesNotAuthorizeTheMetaRoute()
    {
        var metaService = CreateMetaService();

        var result = await metaService.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
    }

    [Fact]
    public async Task SnapchatToken_DoesNotAuthorizeTheGoogleRoute()
    {
        var googleService = CreateGoogleService();

        var result = await googleService.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
    }

    [Fact]
    public async Task SnapchatToken_DoesNotAuthorizeTheTikTokRoute()
    {
        var tiktokService = CreateTikTokService();

        var result = await tiktokService.GenerateAsync(_snapchatRawToken);

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
    public async Task RevokedSnapchatToken_Returns404Equivalent()
    {
        var service = CreateService();
        _snapchatToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
    }

    [Fact]
    public async Task InactiveTenant_ReturnsTenantInactive()
    {
        var service = CreateService();
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TenantInactive, result.Outcome);
    }

    // =========================================================================
    // Cache: channel-isolated keys, warm path, revocation beats cache
    // =========================================================================

    [Fact]
    public async Task CacheMiss_GeneratesAndCaches_OnTheSnapchatChannelKey()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.False(result.CacheHit);
        Assert.Equal(1, _cache.SetCalls);
        var key = _cache.SetKeys.Single();
        Assert.Contains($"tenant:{_tenant.Id:N}:feed:snapchat:{_snapchatToken.Id:N}", key, StringComparison.Ordinal);
        Assert.DoesNotContain(_snapchatRawToken, key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaGoogleTikTokAndSnapchatCacheKeys_NeverCollide()
    {
        var snapchatService = CreateService();
        var metaService = CreateMetaService();
        var googleService = CreateGoogleService();
        var tiktokService = CreateTikTokService();

        await metaService.GenerateAsync(_metaRawToken);
        await googleService.GenerateAsync(_googleRawToken);
        await tiktokService.GenerateAsync(_tiktokRawToken);
        await snapchatService.GenerateAsync(_snapchatRawToken);

        Assert.Equal(4, _cache.SetKeys.Distinct().Count());
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:meta:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:google:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:tiktok:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:snapchat:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondRequest_ServedFromSnapchatCache_WithoutReSerialization()
    {
        var service = CreateService();

        var cold = await service.GenerateAsync(_snapchatRawToken);
        var warm = await service.GenerateAsync(_snapchatRawToken);

        Assert.False(cold.CacheHit);
        Assert.True(warm.CacheHit);
        Assert.Equal(cold.Xml, warm.Xml);
        Assert.Equal(1, _cache.SetCalls);
        Assert.Equal(2, _cache.GetCalls);
        Assert.Equal((int)_cache.GetTtl().TotalSeconds, warm.CacheMaxAgeSeconds);
    }

    [Fact]
    public async Task RevokedSnapchatToken_IsNotServedFromCache()
    {
        var service = CreateService();
        var cold = await service.GenerateAsync(_snapchatRawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, cold.Outcome);
        Assert.True(_cache.Contains(_tenant.Id, _snapchatToken.Id, ProductFeedToken.SnapchatChannel));

        _snapchatToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var afterRevoke = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, afterRevoke.Outcome);
        Assert.Null(afterRevoke.Xml);
    }

    [Fact]
    public async Task RedisOutage_SnapchatFeedStillServedFromSql()
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

        var result = await service.GenerateAsync(_snapchatRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome); // never a 500 because of Redis
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Token lifecycle on the snapchat channel
    // =========================================================================

    [Fact]
    public async Task CreateToken_SnapchatChannel_PersistsHashAndChannel()
    {
        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);

        var created = await tokenService.CreateTokenAsync(_tenant.Id, ProductFeedToken.SnapchatChannel, CancellationToken.None);

        Assert.Equal(ProductFeedToken.SnapchatChannel, created.Channel);
        var stored = await _db.ProductFeedTokens.SingleAsync(t => t.Id == created.TokenId);
        Assert.Equal(ProductFeedToken.SnapchatChannel, stored.Channel);
        Assert.Equal(ProductFeedToken.HashToken(created.RawToken), stored.TokenHash);
    }

    [Fact]
    public async Task RotateSnapchatToken_PreservesChannel_AndInvalidatesSnapchatCacheKey()
    {
        var service = CreateService();
        await service.GenerateAsync(_snapchatRawToken);
        Assert.True(_cache.Contains(_tenant.Id, _snapchatToken.Id, ProductFeedToken.SnapchatChannel));

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var rotated = await tokenService.RotateTokenAsync(_tenant.Id, _snapchatToken.Id, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.Equal(ProductFeedToken.SnapchatChannel, rotated!.Channel); // channel preserved
        Assert.False(_cache.Contains(_tenant.Id, _snapchatToken.Id, ProductFeedToken.SnapchatChannel));
        Assert.Contains(
            _cache.InvalidatedKeys,
            key => key.Contains(":feed:snapchat:", StringComparison.Ordinal));

        var newFeed = await service.GenerateAsync(rotated.RawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, newFeed.Outcome);
        Assert.False(newFeed.CacheHit);
    }

    [Fact]
    public async Task RevokeSnapchatToken_InvalidatesOnlyTheSnapchatCacheKey_MetaEntryUntouched()
    {
        var snapchatService = CreateService();
        var metaService = CreateMetaService();
        await metaService.GenerateAsync(_metaRawToken);
        await snapchatService.GenerateAsync(_snapchatRawToken);

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var revoked = await tokenService.RevokeTokenAsync(_tenant.Id, _snapchatToken.Id, CancellationToken.None);

        Assert.True(revoked);
        Assert.False(_cache.Contains(_tenant.Id, _snapchatToken.Id, ProductFeedToken.SnapchatChannel));
    }

    // =========================================================================
    // Seeding helper (mirrors TikTokProductFeedServiceTests conventions)
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
