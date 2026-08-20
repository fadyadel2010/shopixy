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
/// FEED-2: Google channel adapter contract — strict channel-scoped token
/// authorization (cross-channel tokens never authorize), shared projection
/// reuse, channel-isolated cache keys (Meta/Google never collide),
/// validation-before-cache ordering, and token lifecycle on the google channel.
/// </summary>
public sealed class GoogleProductFeedServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private string _metaRawToken = null!;
    private string _googleRawToken = null!;
    private ProductFeedToken _googleToken = null!;
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
            Name = "Google Feed Store",
            Slug = "googlefeed",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _metaRawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _metaRawToken));

        _googleRawToken = ProductFeedTokenService.GenerateRawToken();
        _googleToken = ProductFeedToken.Create(_tenant.Id, ProductFeedToken.GoogleChannel, _googleRawToken);
        _db.ProductFeedTokens.Add(_googleToken);

        await AddEligibleProductAsync("Google Product", 99m);
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

    private GoogleProductFeedService CreateService(IMetaFeedCache? cache = null) => new(
        _db,
        CreateProjection(cache),
        new GoogleFeedXmlSerializer(),
        CreateUrlResolver(),
        NullLogger<GoogleProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    // =========================================================================
    // Happy path: google token -> Google RSS via the shared projection
    // =========================================================================

    [Fact]
    public async Task ValidGoogleToken_ReturnsGoogleRss_FromTheSharedProjection()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:g=\"http://base.google.com/ns/1.0\"", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Products feed for Google Feed Store - Google", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Google Product", result.Xml, StringComparison.Ordinal);
        Assert.Contains("<g:identifier_exists>no</g:identifier_exists>", result.Xml, StringComparison.Ordinal);
        Assert.Equal(1, result.ItemCount);
    }

    [Fact]
    public async Task GoogleAndMetaFeeds_ContainTheSameProductTruth_DifferentVocabulary()
    {
        var googleService = CreateService();
        var metaService = CreateProjection();

        var google = await googleService.GenerateAsync(_googleRawToken);
        var meta = await metaService.GenerateAsync(_metaRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, google.Outcome);
        Assert.Equal(MetaFeedGenerationOutcome.Success, meta.Outcome);

        // Identical projection semantics: same item count, same product.
        Assert.Equal(meta.ItemCount, google.ItemCount);
        Assert.Contains("Google Product", meta.Xml, StringComparison.Ordinal);
        // Vocabulary diverges only where the channel contract requires it.
        Assert.Contains("- Google", google.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("- Google", meta.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("g:mpn", google.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Channel-scoped authorization: cross-channel tokens NEVER authorize
    // =========================================================================

    [Fact]
    public async Task MetaToken_DoesNotAuthorizeTheGoogleRoute()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_metaRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, _cache.GetCalls); // never reaches the cache
    }

    [Fact]
    public async Task GoogleToken_DoesNotAuthorizeTheMetaRoute()
    {
        var metaService = CreateProjection();

        var result = await metaService.GenerateAsync(_googleRawToken);

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
    public async Task RevokedGoogleToken_Returns404Equivalent()
    {
        var service = CreateService();
        _googleToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
    }

    [Fact]
    public async Task InactiveTenant_ReturnsTenantInactive()
    {
        var service = CreateService();
        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TenantInactive, result.Outcome);
    }

    // =========================================================================
    // Cache: channel-isolated keys, warm path, revocation beats cache
    // =========================================================================

    [Fact]
    public async Task CacheMiss_GeneratesAndCaches_OnTheGoogleChannelKey()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.False(result.CacheHit);
        Assert.Equal(1, _cache.SetCalls);
        var key = _cache.SetKeys.Single();
        Assert.Contains($"tenant:{_tenant.Id:N}:feed:google:{_googleToken.Id:N}", key, StringComparison.Ordinal);
        Assert.DoesNotContain(_googleRawToken, key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetaAndGoogleCacheEntries_NeverCollide()
    {
        var googleService = CreateService();
        var metaService = CreateProjection();

        var meta = await metaService.GenerateAsync(_metaRawToken);
        var google = await googleService.GenerateAsync(_googleRawToken);

        Assert.Equal(2, _cache.SetKeys.Distinct().Count());
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:meta:", StringComparison.Ordinal));
        Assert.Contains(_cache.SetKeys, key => key.Contains(":feed:google:", StringComparison.Ordinal));

        // Each channel warms ONLY its own key; neither ever serves the other's XML.
        var warmMeta = await metaService.GenerateAsync(_metaRawToken);
        var warmGoogle = await googleService.GenerateAsync(_googleRawToken);
        Assert.True(warmMeta.CacheHit);
        Assert.True(warmGoogle.CacheHit);
        Assert.Equal(meta.Xml, warmMeta.Xml);
        Assert.Equal(google.Xml, warmGoogle.Xml);
        Assert.DoesNotContain("- Google", warmMeta.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRequest_ServedFromGoogleCache_WithoutReSerialization()
    {
        var service = CreateService();

        var cold = await service.GenerateAsync(_googleRawToken);
        var warm = await service.GenerateAsync(_googleRawToken);

        Assert.False(cold.CacheHit);
        Assert.True(warm.CacheHit);
        Assert.Equal(cold.Xml, warm.Xml);
        Assert.Equal(1, _cache.SetCalls);
        Assert.Equal(2, _cache.GetCalls);
        Assert.Equal((int)_cache.GetTtl().TotalSeconds, warm.CacheMaxAgeSeconds);
    }

    [Fact]
    public async Task RevokedGoogleToken_IsNotServedFromCache()
    {
        var service = CreateService();
        var cold = await service.GenerateAsync(_googleRawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, cold.Outcome);
        Assert.True(_cache.Contains(_tenant.Id, _googleToken.Id, ProductFeedToken.GoogleChannel));

        _googleToken.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var afterRevoke = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, afterRevoke.Outcome);
        Assert.Null(afterRevoke.Xml);
    }

    [Fact]
    public async Task RedisOutage_GoogleFeedStillServedFromSql()
    {
        // Failure isolation lives in the real MetaFeedCache wrapper (Feed-1B
        // contract) — mirror the Meta test: wrapper over a throwing Redis.
        var redis = new Moq.Mock<IRedisJsonCacheService>(Moq.MockBehavior.Strict);
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

        var result = await service.GenerateAsync(_googleRawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome); // never a 500 because of Redis
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
    }

    // =========================================================================
    // Token lifecycle on the google channel
    // =========================================================================

    [Fact]
    public async Task CreateToken_GoogleChannel_PersistsHashAndChannel()
    {
        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);

        var created = await tokenService.CreateTokenAsync(_tenant.Id, ProductFeedToken.GoogleChannel, CancellationToken.None);

        Assert.Equal(ProductFeedToken.GoogleChannel, created.Channel);
        var stored = await _db.ProductFeedTokens.SingleAsync(t => t.Id == created.TokenId);
        Assert.Equal(ProductFeedToken.GoogleChannel, stored.Channel);
        Assert.Equal(ProductFeedToken.HashToken(created.RawToken), stored.TokenHash);
    }

    [Fact]
    public async Task CreateToken_UnsupportedChannel_IsRejected()
    {
        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenService.CreateTokenAsync(_tenant.Id, "invalid_channel", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenService.CreateTokenAsync(_tenant.Id, string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task RotateGoogleToken_PreservesChannel_AndInvalidatesGoogleCacheKey()
    {
        var service = CreateService();
        await service.GenerateAsync(_googleRawToken);
        Assert.True(_cache.Contains(_tenant.Id, _googleToken.Id, ProductFeedToken.GoogleChannel));

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var rotated = await tokenService.RotateTokenAsync(_tenant.Id, _googleToken.Id, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.Equal(ProductFeedToken.GoogleChannel, rotated!.Channel);       // channel preserved
        Assert.False(_cache.Contains(_tenant.Id, _googleToken.Id, ProductFeedToken.GoogleChannel));
        Assert.Contains(
            _cache.InvalidatedKeys,
            key => key.Contains(":feed:google:", StringComparison.Ordinal)); // channel-specific invalidation

        var newFeed = await service.GenerateAsync(rotated.RawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, newFeed.Outcome);
        Assert.False(newFeed.CacheHit);
    }

    [Fact]
    public async Task RevokeGoogleToken_InvalidatesOnlyTheGoogleCacheKey_MetaEntryUntouched()
    {
        var googleService = CreateService();
        var metaService = CreateProjection();
        await metaService.GenerateAsync(_metaRawToken);
        await googleService.GenerateAsync(_googleRawToken);

        var metaToken = await _db.ProductFeedTokens.SingleAsync(t => t.Channel == ProductFeedToken.MetaChannel);

        var tokenService = new ProductFeedTokenService(_db, NullLogger<ProductFeedTokenService>.Instance, _cache);
        var revoked = await tokenService.RevokeTokenAsync(_tenant.Id, _googleToken.Id, CancellationToken.None);

        Assert.True(revoked);
        Assert.False(_cache.Contains(_tenant.Id, _googleToken.Id, ProductFeedToken.GoogleChannel));
        Assert.True(_cache.Contains(_tenant.Id, metaToken.Id, ProductFeedToken.MetaChannel)); // untouched
    }

    // =========================================================================
    // Seeding helper (mirrors MetaFeedCachingTests conventions)
    // =========================================================================

    private async Task AddEligibleProductAsync(string name, decimal unitPrice)
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
            TrackStock = false
        };
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
