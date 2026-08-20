using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Feeds;
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
/// FEED-1B: Rendered-XML cache contract — validation-before-lookup ordering,
/// tenant/token key isolation, miss/hit behavior, Redis failure isolation,
/// corruption treated as miss, and revoke/rotate invalidation.
/// </summary>
public sealed class MetaFeedCachingTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private ProductFeedToken _token = null!;
    private string _rawToken = null!;
    private InMemoryMetaFeedCache _cache = null!;
    private CountingSerializer _serializer = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Cached Feed Store",
            Slug = "cachedfeed",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _rawToken = ProductFeedTokenService.GenerateRawToken();
        _token = ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _rawToken);
        _db.ProductFeedTokens.Add(_token);

        await AddEligibleProductAsync("Cached Product", 120m);
        await _db.SaveChangesAsync();

        _cache = new InMemoryMetaFeedCache();
        _serializer = new CountingSerializer();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private MetaProductFeedService CreateService(IMetaFeedCache? cache = null) => new(
        _db,
        _serializer,
        new StorefrontUrlResolver(
            new ConfigurationBuilder().Build(),
            NullLogger<StorefrontUrlResolver>.Instance,
            null!),
        NullLogger<MetaProductFeedService>.Instance,
        cache ?? _cache,
        FeedTestFactory.CreateTelemetry());

    // =========================================================================
    // Miss -> generate -> cache
    // =========================================================================

    [Fact]
    public async Task CacheMiss_GeneratesFeed_AndCachesRenderedXml()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.False(result.CacheHit);
        Assert.Equal(1, _cache.SetCalls);
        Assert.Contains(_token.Id.ToString("N"), _cache.SetKeys.Single(), StringComparison.Ordinal);
        Assert.Contains(_tenant.Id.ToString("N"), _cache.SetKeys.Single(), StringComparison.Ordinal);

        var cached = await _cache.TryGetAsync(_tenant.Id, _token.Id);
        Assert.Equal(MetaFeedCacheLookupOutcome.Hit, cached.Outcome);
        Assert.Equal(result.Xml, cached.Payload!.Xml);
        Assert.Equal(result.ItemCount, cached.Payload.ItemCount);
    }

    [Fact]
    public async Task CacheKey_NeverContainsRawToken()
    {
        var service = CreateService();

        await service.GenerateAsync(_rawToken);

        var key = _cache.SetKeys.Single();
        Assert.DoesNotContain(_rawToken, key, StringComparison.Ordinal);
    }

    // =========================================================================
    // Hit -> no regeneration, byte-identical output
    // =========================================================================

    [Fact]
    public async Task SecondRequest_ServedFromCache_WithoutReSerialization()
    {
        var service = CreateService();

        var cold = await service.GenerateAsync(_rawToken);
        var warm = await service.GenerateAsync(_rawToken);

        Assert.False(cold.CacheHit);
        Assert.True(warm.CacheHit);
        Assert.Equal(cold.Xml, warm.Xml);          // byte-compatible output
        Assert.Equal(1, _serializer.SerializeCalls); // projection/serialization skipped
        Assert.Equal(1, _cache.SetCalls);
        Assert.Equal(2, _cache.GetCalls);
    }

    [Fact]
    public async Task CachedPayloadPreservesDiagnostics_ItemAndExcludedCounts()
    {
        var service = CreateService();

        var cold = await service.GenerateAsync(_rawToken);
        var warm = await service.GenerateAsync(_rawToken);

        Assert.Equal(cold.ItemCount, warm.ItemCount);
        Assert.Equal(cold.ExcludedItemCount, warm.ExcludedItemCount);
        Assert.Equal((int)_cache.GetTtl().TotalSeconds, warm.CacheMaxAgeSeconds);
    }

    // =========================================================================
    // Tenant / token isolation
    // =========================================================================

    [Fact]
    public async Task CacheEntries_AreIsolatedPerTenantAndToken()
    {
        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Other Store",
            Slug = "otherstore",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://other.example.com"
        };
        _db.Tenants.Add(otherTenant);
        var otherRawToken = ProductFeedTokenService.GenerateRawToken();
        var otherToken = ProductFeedToken.Create(otherTenant.Id, ProductFeedToken.MetaChannel, otherRawToken);
        _db.ProductFeedTokens.Add(otherToken);
        await _db.SaveChangesAsync();

        var service = CreateService();
        var first = await service.GenerateAsync(_rawToken);
        var second = await service.GenerateAsync(otherRawToken);

        Assert.False(first.CacheHit);
        Assert.False(second.CacheHit); // no cross-tenant cache sharing
        Assert.Contains("Cached Feed Store", first.Xml, StringComparison.Ordinal);
        Assert.Contains("Other Store", second.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Store", first.Xml, StringComparison.Ordinal);
        Assert.Equal(2, _cache.SetKeys.Distinct().Count());
    }

    // =========================================================================
    // Validation-before-lookup: revocation beats the cache
    // =========================================================================

    [Fact]
    public async Task RevokedToken_IsNotServedFromCache()
    {
        var service = CreateService();
        var cold = await service.GenerateAsync(_rawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, cold.Outcome);
        Assert.True(_cache.Contains(_tenant.Id, _token.Id));

        _token.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var afterRevoke = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, afterRevoke.Outcome);
        Assert.Null(afterRevoke.Xml);
    }

    [Fact]
    public async Task InactiveTenant_IsNotServedFromCache()
    {
        var service = CreateService();
        await service.GenerateAsync(_rawToken);
        Assert.True(_cache.Contains(_tenant.Id, _token.Id));

        _tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.TenantInactive, result.Outcome);
    }

    [Fact]
    public async Task UnknownToken_NeverTouchesTheCache()
    {
        var service = CreateService();

        var result = await service.GenerateAsync(ProductFeedTokenService.GenerateRawToken());

        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, result.Outcome);
        Assert.Equal(0, _cache.GetCalls);
        Assert.Equal(0, _cache.SetCalls);
    }

    // =========================================================================
    // Revoke / rotate invalidation
    // =========================================================================

    [Fact]
    public async Task RevokeToken_InvalidatesCachedFeed()
    {
        var service = CreateService();
        await service.GenerateAsync(_rawToken);
        Assert.True(_cache.Contains(_tenant.Id, _token.Id));

        var tokenService = new ProductFeedTokenService(
            _db,
            NullLogger<ProductFeedTokenService>.Instance,
            _cache);
        var revoked = await tokenService.RevokeTokenAsync(_tenant.Id, _token.Id, CancellationToken.None);

        Assert.True(revoked);
        Assert.Equal(1, _cache.InvalidateCalls);
        Assert.False(_cache.Contains(_tenant.Id, _token.Id));
    }

    [Fact]
    public async Task RotateToken_InvalidatesOldCache_NewTokenStartsCold()
    {
        var service = CreateService();
        await service.GenerateAsync(_rawToken);
        Assert.True(_cache.Contains(_tenant.Id, _token.Id));

        var tokenService = new ProductFeedTokenService(
            _db,
            NullLogger<ProductFeedTokenService>.Instance,
            _cache);
        var rotated = await tokenService.RotateTokenAsync(_tenant.Id, _token.Id, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.False(_cache.Contains(_tenant.Id, _token.Id));          // old entry gone
        Assert.False(_cache.Contains(_tenant.Id, rotated!.TokenId));    // no stale entry for new id

        var newFeed = await service.GenerateAsync(rotated.RawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, newFeed.Outcome);
        Assert.False(newFeed.CacheHit);
    }

    [Fact]
    public async Task RedisOutageDuringInvalidation_DoesNotFailRevoke()
    {
        var service = CreateService();
        await service.GenerateAsync(_rawToken);

        var tokenService = new ProductFeedTokenService(
            _db,
            NullLogger<ProductFeedTokenService>.Instance,
            new ThrowingMetaFeedCache());

        var revoked = await tokenService.RevokeTokenAsync(_tenant.Id, _token.Id, CancellationToken.None);

        Assert.True(revoked); // revocation succeeds even when Redis is down
    }

    // =========================================================================
    // MetaFeedCache wrapper over IRedisJsonCacheService (failure isolation)
    // =========================================================================

    [Fact]
    public async Task RedisReadFailure_IsBypassed_AndFeedStillServedFromSql()
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

        var result = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome); // never a 500 because of Redis
        Assert.NotNull(result.Xml);
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptOrMissingCachedPayload_TreatedAsMiss_AndRegenerated()
    {
        var redis = new Mock<IRedisJsonCacheService>(MockBehavior.Loose);
        // RedisJsonCacheService semantics: corruption -> payload deleted + null returned.
        redis
            .Setup(r => r.GetAsync<MetaFeedCachePayload>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetaFeedCachePayload?)null);

        var realCache = FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry());

        var (outcome, payload) = await realCache.TryGetAsync(_tenant.Id, _token.Id);

        Assert.Equal(MetaFeedCacheLookupOutcome.Miss, outcome);
        Assert.Null(payload);
    }

    [Fact]
    public async Task CacheTtl_IsClampedToTheFeed1BRange()
    {
        var redis = new Mock<IRedisJsonCacheService>(MockBehavior.Loose);

        Assert.Equal(TimeSpan.FromMinutes(5),
            FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry(), ttlMinutes: 1).GetTtl());
        Assert.Equal(TimeSpan.FromMinutes(7),
            FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry(), ttlMinutes: 7).GetTtl());
        Assert.Equal(TimeSpan.FromMinutes(10),
            FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry(), ttlMinutes: 30).GetTtl());
    }

    [Fact]
    public async Task CacheSet_UsesConfiguredTtl_AndTenantScopedKey()
    {
        var redis = new Mock<IRedisJsonCacheService>(MockBehavior.Strict);
        string? capturedKey = null;
        TimeSpan capturedTtl = TimeSpan.Zero;
        redis
            .Setup(r => r.SetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetaFeedCachePayload>(),
                It.IsAny<TimeSpan>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((string key, string _, MetaFeedCachePayload _, TimeSpan ttl,
                DateTime? _, DateTime? _, int _, CancellationToken _) =>
            {
                capturedKey = key;
                capturedTtl = ttl;
            })
            .Returns(Task.CompletedTask);

        var realCache = FeedTestFactory.CreateRealCacheOver(redis.Object, FeedTestFactory.CreateTelemetry(), ttlMinutes: 5);
        await realCache.SetAsync(
            _tenant.Id,
            _token.Id,
            new MetaFeedCachePayload { Xml = "<rss/>", ItemCount = 1, GeneratedAtUtc = DateTime.UtcNow });

        Assert.NotNull(capturedKey);
        Assert.Contains($"tenant:{_tenant.Id:N}:feed:meta:{_token.Id:N}", capturedKey, StringComparison.Ordinal);
        Assert.DoesNotContain(_rawToken, capturedKey, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(5), capturedTtl);
    }

    // =========================================================================
    // Seeding helper (mirrors MetaProductFeedQueryTests conventions)
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

    /// <summary>Serializer spy proving the warm path skips serialization.</summary>
    private sealed class CountingSerializer : IMetaFeedXmlSerializer
    {
        private readonly MetaFeedXmlSerializer _inner = new();

        public int SerializeCalls { get; private set; }

        public string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items)
        {
            SerializeCalls++;
            return _inner.Serialize(tenantName, channelLink, items);
        }
    }
}
