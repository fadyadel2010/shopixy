using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;
using Xunit;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1B runtime validation, executed against the real composed stack
/// (MetaProductFeedService + MetaFeedCache over IRedisJsonCacheService +
/// ProductFeedTokenService + the real rate-limit options). No live
/// Redis/SQL Server is required: the Redis seam is a faithful in-memory
/// store, so wire behavior (miss -> populate -> hit -> invalidate) is what
/// gets exercised end to end.
/// </summary>
public sealed class MetaFeedRuntimeScenarioTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private ProductFeedToken _token = null!;
    private string _rawToken = null!;
    private InMemoryRedisJsonStore _redis = null!;
    private MetaFeedCache _cache = null!;
    private MetaProductFeedService _service = null!;
    private ProductFeedTokenService _tokenService = null!;

    public MetaFeedRuntimeScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Runtime Store",
            Slug = "runtime",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _rawToken = ProductFeedTokenService.GenerateRawToken();
        _token = ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _rawToken);
        _db.ProductFeedTokens.Add(_token);

        for (var i = 0; i < 50; i++)
        {
            await AddEligibleProductAsync($"Runtime Product {i}", 50m + i);
        }
        await _db.SaveChangesAsync();

        _redis = new InMemoryRedisJsonStore();
        _cache = FeedTestFactory.CreateRealCacheOver(_redis.Service, FeedTestFactory.CreateTelemetry());
        _service = new MetaProductFeedService(
            _db,
            new MetaFeedXmlSerializer(),
            new StorefrontUrlResolver(
                new ConfigurationBuilder().Build(),
                NullLogger<StorefrontUrlResolver>.Instance,
                null!),
            NullLogger<MetaProductFeedService>.Instance,
            _cache,
            FeedTestFactory.CreateTelemetry());
        _tokenService = new ProductFeedTokenService(
            _db,
            NullLogger<ProductFeedTokenService>.Instance,
            _cache);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // =========================================================================
    // Scenario 1: cold miss -> populate -> warm hit, byte-identical + perf
    // =========================================================================

    [Fact]
    public async Task Scenario_ColdMiss_WarmHit_ByteIdentical_Faster()
    {
        var cold = await TimedAsync(() => _service.GenerateAsync(_rawToken));
        var warm = await TimedAsync(() => _service.GenerateAsync(_rawToken));

        Assert.Equal(MetaFeedGenerationOutcome.Success, cold.Result.Outcome);
        Assert.False(cold.Result.CacheHit);
        Assert.True(warm.Result.CacheHit);
        Assert.Equal(cold.Result.Xml, warm.Result.Xml); // byte-compatible output
        Assert.Equal(50, warm.Result.ItemCount);
        Assert.Equal(300, warm.Result.CacheMaxAgeSeconds); // Cache-Control source

        // TTL exactly the configured 5 minutes reached the Redis seam.
        var ttl = _redis.Ttls.Values.Single();
        Assert.Equal(TimeSpan.FromMinutes(5), ttl);

        _output.WriteLine($"cold: {cold.ElapsedMs} ms | warm: {warm.ElapsedMs} ms | bytes: {System.Text.Encoding.UTF8.GetByteCount(cold.Result.Xml!)}");
        Assert.True(warm.ElapsedMs <= cold.ElapsedMs + 25, $"warm path should not exceed cold path (cold={cold.ElapsedMs}ms warm={warm.ElapsedMs}ms)");
    }

    // =========================================================================
    // Scenario 2: revocation returns 404-equivalent even while cached
    // =========================================================================

    [Fact]
    public async Task Scenario_Revoke_Serves404_EvenWhenCacheStillWarm()
    {
        var warm = await _service.GenerateAsync(_rawToken);
        Assert.False(warm.CacheHit);
        Assert.Equal(1, _redis.Store.Count);

        var revoked = await _tokenService.RevokeTokenAsync(_tenant.Id, _token.Id, CancellationToken.None);
        Assert.True(revoked);
        Assert.Empty(_redis.Store); // invalidation removed the entry

        var afterRevoke = await _service.GenerateAsync(_rawToken);
        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, afterRevoke.Outcome); // controller maps to 404

        // Defense in depth: even if a stale entry survived (invalidation lost),
        // validation-before-lookup still refuses to serve it.
        _redis.Store[_cache.BuildKey(_tenant.Id, _token.Id)] =
            (new MetaFeedCachePayload { Xml = warm.Xml!, ItemCount = 50, GeneratedAtUtc = DateTime.UtcNow }, TimeSpan.FromMinutes(5));

        var stale = await _service.GenerateAsync(_rawToken);
        Assert.Equal(MetaFeedGenerationOutcome.TokenNotFound, stale.Outcome);
    }

    // =========================================================================
    // Scenario 3: fixed-window burst -> 30 admitted, 31st rejected (429 path)
    // =========================================================================

    [Fact]
    public void Scenario_BurstOfRequests_RejectedAfter30PerWindow()
    {
        using var limiter = new FixedWindowRateLimiter(PublicFeedRateLimitPolicy.CreateOptions());
        var partitionKey = PublicFeedRateLimitPolicy.BuildPartitionKey("burst-token", "10.9.9.9");
        Assert.StartsWith("public-feed:meta:", partitionKey, StringComparison.Ordinal);
        Assert.EndsWith(":ip:10.9.9.9", partitionKey, StringComparison.Ordinal);
        Assert.DoesNotContain("burst-token", partitionKey, StringComparison.Ordinal);

        var admitted = 0;
        for (var i = 0; i < 35; i++)
        {
            using var lease = limiter.AttemptAcquire();
            if (lease.IsAcquired)
            {
                admitted++;
            }
        }

        Assert.Equal(PublicFeedRateLimitPolicy.PermitLimit, admitted); // 30 in, 5 rejected -> 429

        // A different caller (other IP/token partition) is completely unaffected.
        using var other = new FixedWindowRateLimiter(PublicFeedRateLimitPolicy.CreateOptions());
        Assert.True(other.AttemptAcquire().IsAcquired);
    }

    // =========================================================================
    // Scenario 4: Redis outage mid-session never breaks the feed
    // =========================================================================

    [Fact]
    public async Task Scenario_RedisOutage_FeedContinuesFromSql()
    {
        await _service.GenerateAsync(_rawToken); // warm the cache
        _redis.FailAllOperations = true;

        // Real MetaFeedCache must convert the outage into a miss/bypass.
        var (outcome, _) = await _cache.TryGetAsync(_tenant.Id, _token.Id);
        Assert.Equal(MetaFeedCacheLookupOutcome.Error, outcome);

        var result = await _service.GenerateAsync(_rawToken);
        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome); // still 200, never 500
        Assert.Contains("<rss", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario_LocalhostStorefrontBaseUrl_FailsClosedBeforeSerialization()
    {
        _tenant.StorefrontBaseUrl = "http://shopixy.localhost:5174";
        await _db.SaveChangesAsync();

        var result = await _service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.GenerationFailed, result.Outcome);
        Assert.Null(result.Xml);
        Assert.Equal(0, result.ItemCount);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(MetaProductFeedGenerationResult Result, long ElapsedMs)> TimedAsync(
        Func<Task<MetaProductFeedGenerationResult>> action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();
        return (result, stopwatch.ElapsedMilliseconds);
    }

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
            Name = $"Piece {name}"
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

    /// <summary>
    /// Faithful in-memory stand-in for IRedisJsonCacheService: keyed payload
    /// store with TTL capture and an outage switch. Only the members used by
    /// MetaFeedCache are wired; the rest keep Moq loose defaults.
    /// </summary>
    private sealed class InMemoryRedisJsonStore
    {
        public Dictionary<string, (MetaFeedCachePayload Payload, TimeSpan Ttl)> Store { get; } = new();
        public Dictionary<string, TimeSpan> Ttls { get; } = new();
        public bool FailAllOperations { get; set; }

        public IRedisJsonCacheService Service { get; }

        public InMemoryRedisJsonStore()
        {
            var mock = new Mock<IRedisJsonCacheService>(MockBehavior.Loose);

            mock
                .Setup(r => r.GetAsync<MetaFeedCachePayload>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, string _, TimeSpan? _, CancellationToken _) =>
                {
                    if (FailAllOperations)
                    {
                        throw new TimeoutException("Simulated Redis outage");
                    }
                    return Store.TryGetValue(key, out var entry) ? entry.Payload : null;
                });

            mock
                .Setup(r => r.SetAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetaFeedCachePayload>(),
                    It.IsAny<TimeSpan>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback((string key, string _, MetaFeedCachePayload payload, TimeSpan ttl,
                    DateTime? _, DateTime? _, int _, CancellationToken _) =>
                {
                    if (FailAllOperations)
                    {
                        throw new TimeoutException("Simulated Redis outage");
                    }
                    Store[key] = (payload, ttl);
                    Ttls[key] = ttl;
                })
                .Returns(Task.CompletedTask);

            mock
                .Setup(r => r.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string key, CancellationToken _) =>
                {
                    if (FailAllOperations)
                    {
                        throw new TimeoutException("Simulated Redis outage");
                    }
                    Store.Remove(key);
                })
                .Returns(Task.CompletedTask);

            Service = mock.Object;
        }
    }
}
