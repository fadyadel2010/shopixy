using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class M45ProductLookupValidationCacheTests
{
    [Fact]
    public async Task ProductId_Positive_ColdLoadsOnceAndWarmLoadsZero()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _) => Task.FromResult<ProductDocument?>(
            Document(tenantId, productId, Interlocked.Increment(ref loads), "widget"));

        var cold = await cache.GetOrAddByIdAsync(tenantId, productId, Load);
        var warm = await cache.GetOrAddByIdAsync(tenantId, productId, Load);

        Assert.Equal(1, loads);
        Assert.Equal(productId, cold!.ProductId);
        Assert.Equal(productId, warm!.ProductId);
    }

    [Fact]
    public async Task Slug_Positive_ColdLoadsOnceAndWarmLoadsZero()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _) => Task.FromResult<ProductDocument?>(
            Document(tenantId, productId, Interlocked.Increment(ref loads), "widget"));

        await cache.GetOrAddBySlugAsync(tenantId, "widget", true, Load);
        var warm = await cache.GetOrAddBySlugAsync(tenantId, "widget", true, Load);

        Assert.Equal(1, loads);
        Assert.Equal(productId, warm!.ProductId);
        Assert.Equal("widget", warm.Slug);
    }

    [Fact]
    public async Task ProductId_Negative_ColdLoadsOnceAndWarmLoadsZero()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<ProductDocument?>(null);
        }

        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task NegativeTtlExpiry_Reloads()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state, negativeTtlSeconds: 0.1);
        var loads = 0;
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<ProductDocument?>(null);
        }

        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        await Task.Delay(250);
        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task PositiveTtlExpiry_Reloads()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state, positiveTtlSeconds: 0.1);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _) => Task.FromResult<ProductDocument?>(
            Document(tenantId, productId, Interlocked.Increment(ref loads), "ttl"));

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        await Task.Delay(250);
        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task CreationOrReactivation_GenerationAdvanceInvalidatesNegativeImmediately()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        ProductDocument? current = null;
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(current);
        }

        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        current = Document(tenantId, productId, 2, "created");
        state.Advance(tenantId);

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task UpdateOrDelete_GenerationAdvanceEvictsPositiveImmediately()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        ProductDocument? current = Document(tenantId, productId, 1, "live");
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(current);
        }

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        current = null;
        state.Advance(tenantId);

        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task SlugRename_InvalidatesOldAndNewSlugNegative()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var oldExists = true;
        var newExists = false;
        var loads = 0;

        Task<ProductDocument?> LoadOld(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(oldExists ? Document(tenantId, productId, 1, "old") : null);
        }

        Task<ProductDocument?> LoadNew(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(newExists ? Document(tenantId, productId, 2, "new") : null);
        }

        Assert.NotNull(await cache.GetOrAddBySlugAsync(tenantId, "old", true, LoadOld));
        Assert.Null(await cache.GetOrAddBySlugAsync(tenantId, "new", true, LoadNew));

        oldExists = false;
        newExists = true;
        state.Advance(tenantId);

        Assert.Null(await cache.GetOrAddBySlugAsync(tenantId, "old", true, LoadOld));
        Assert.NotNull(await cache.GetOrAddBySlugAsync(tenantId, "new", true, LoadNew));
        Assert.Equal(4, loads);
    }

    [Fact]
    public async Task StorefrontInvalidationOwner_AdvancesLookupGeneration()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        ProductDocument? current = Document(tenantId, productId, 1, "wired");
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult(current);
        }

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        current = null;

        var invalidator = new StorefrontProductCacheInvalidator(
            state.Cache.Object,
            Options.Create(new RedisSettings { InstanceName = "b1-tests" }));
        await invalidator.InvalidateProductAsync(tenantId, productId, "wired");

        Assert.Null(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task TenantAndSlugScopes_DoNotCollide()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var loads = 0;

        var a = await cache.GetOrAddBySlugAsync(tenantA, "same", true, _ =>
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<ProductDocument?>(Document(tenantA, productA, 1, "same"));
        });
        var b = await cache.GetOrAddBySlugAsync(tenantB, "same", true, _ =>
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<ProductDocument?>(Document(tenantB, productB, 1, "same"));
        });

        Assert.Equal(2, loads);
        Assert.Equal(tenantA, a!.TenantId);
        Assert.Equal(tenantB, b!.TenantId);
        Assert.NotEqual(a.ProductId, b.ProductId);
    }

    [Fact]
    public async Task ExactSlugAndVisibilityScopes_DoNotCollide()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _) => Task.FromResult<ProductDocument?>(
            Document(tenantId, Guid.NewGuid(), Interlocked.Increment(ref loads), "scope"));

        await cache.GetOrAddBySlugAsync(tenantId, "Case", true, Load);
        await cache.GetOrAddBySlugAsync(tenantId, "case", true, Load);
        await cache.GetOrAddBySlugAsync(tenantId, "Case", false, Load);
        await cache.GetOrAddBySlugAsync(tenantId, "Case", true, Load);

        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task PositiveEntry_CachesIdentityOnly_NotMutableAuthority()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var source = Document(tenantId, productId, 1, "identity");
        source.Price = 999m;
        source.StockQuantity = 999;
        source.Variants.Add(new ProductVariantDocument { VariantId = Guid.NewGuid(), TotalStock = 999 });

        var result = await cache.GetOrAddByIdAsync(
            tenantId,
            productId,
            _ => Task.FromResult<ProductDocument?>(source));

        Assert.Equal(productId, result!.ProductId);
        Assert.Equal(0m, result.Price);
        Assert.Equal(0, result.StockQuantity);
        Assert.Empty(result.Variants);
    }

    [Fact]
    public async Task MismatchedCandidate_IsNeverCached()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<ProductDocument?>(Document(Guid.NewGuid(), productId, 1, "wrong"));
        }

        await cache.GetOrAddByIdAsync(tenantId, productId, Load);
        await cache.GetOrAddByIdAsync(tenantId, productId, Load);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task GenerationReadFailure_BypassesCacheAndPreservesMongoBehavior()
    {
        var state = new GenerationState { ThrowReads = true };
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var loads = 0;

        Task<ProductDocument?> Load(CancellationToken _) => Task.FromResult<ProductDocument?>(
            Document(tenantId, productId, Interlocked.Increment(ref loads), "fallback"));

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task InvalidationRacingLoad_PreventsStaleCacheWrite()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;

        async Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            entered.TrySetResult();
            await release.Task;
            return Document(tenantId, productId, loads, "race");
        }

        var first = cache.GetOrAddByIdAsync(tenantId, productId, Load);
        await entered.Task;
        state.Advance(tenantId);
        release.TrySetResult();
        Assert.NotNull(await first);

        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task LoaderFailure_IsNotCached()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var loads = 0;
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        Task<ProductDocument?> Fail(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            throw new InvalidOperationException("mongo unavailable");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddByIdAsync(tenantId, productId, Fail));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddByIdAsync(tenantId, productId, Fail));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task ColdConcurrency_UsesSingleFlight()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;

        async Task<ProductDocument?> Load(CancellationToken token)
        {
            Assert.Equal(CancellationToken.None, token);
            Interlocked.Increment(ref loads);
            entered.TrySetResult();
            await release.Task;
            return Document(tenantId, productId, 1, "flight");
        }

        var requests = Enumerable.Range(0, 20)
            .Select(_ => cache.GetOrAddByIdAsync(tenantId, productId, Load))
            .ToArray();
        await entered.Task;
        release.TrySetResult();
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, loads);
        Assert.All(results, Assert.NotNull);
    }

    [Fact]
    public async Task DistinctKeys_RemainIndependent()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var loads = 0;
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();

        var results = await Task.WhenAll(ids.Select(id => cache.GetOrAddByIdAsync(
            tenantId,
            id,
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<ProductDocument?>(Document(tenantId, id, 1, "distinct"));
            })));

        Assert.Equal(10, loads);
        Assert.Equal(ids.OrderBy(id => id), results.Select(result => result!.ProductId).OrderBy(id => id));
    }

    [Fact]
    public async Task CancelledFollower_DoesNotPoisonSharedLoadOrCache()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;

        async Task<ProductDocument?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            entered.TrySetResult();
            await release.Task;
            return Document(tenantId, productId, 1, "cancel");
        }

        var leader = cache.GetOrAddByIdAsync(tenantId, productId, Load);
        var follower = cache.GetOrAddByIdAsync(tenantId, productId, Load);
        using var cts = new CancellationTokenSource();
        var cancelled = cache.GetOrAddByIdAsync(tenantId, productId, Load, cts.Token);
        await entered.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.TrySetResult();

        Assert.NotNull(await leader);
        Assert.NotNull(await follower);
        Assert.NotNull(await cache.GetOrAddByIdAsync(tenantId, productId, Load));
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task FailedLeader_DoesNotPoisonCache()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;

        async Task<ProductDocument?> Fail(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            await release.Task;
            throw new InvalidOperationException("mongo pressure");
        }

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Assert.ThrowsAsync<InvalidOperationException>(
                () => cache.GetOrAddByIdAsync(tenantId, productId, Fail)))
            .ToArray();
        await Task.Delay(50);
        release.TrySetResult();
        await Task.WhenAll(attempts);

        var recovered = await cache.GetOrAddByIdAsync(
            tenantId,
            productId,
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<ProductDocument?>(Document(tenantId, productId, 1, "recovered"));
            });

        Assert.Equal(2, loads);
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task Repository_UsesLookupCacheForIdAndSlug()
    {
        var state = new GenerationState();
        using var cache = CreateCache(state);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new CountingRepository(Document(tenantId, productId, 1, "repository"), cache);

        await repository.FindByProductIdAsync(tenantId, productId);
        await repository.FindByProductIdAsync(tenantId, productId);
        await repository.FindBySlugAsync(tenantId, "repository", true);
        await repository.FindBySlugAsync(tenantId, "repository", true);

        Assert.Equal(1, repository.IdLoads);
        Assert.Equal(1, repository.SlugLoads);
        Assert.True(repository.LastOnlineVisibleOnly);
    }

    private static ProductLookupValidationCache CreateCache(
        GenerationState state,
        double positiveTtlSeconds = 30,
        double negativeTtlSeconds = 5,
        int maxEntries = 8192)
    {
        return new ProductLookupValidationCache(
            state.Cache.Object,
            Options.Create(new RedisSettings { InstanceName = "b1-tests" }),
            Options.Create(new ProductLookupValidationCacheOptions
            {
                PositiveTtlSeconds = positiveTtlSeconds,
                NegativeTtlSeconds = negativeTtlSeconds,
                MaxEntries = maxEntries
            }),
            NullLogger<ProductLookupValidationCache>.Instance);
    }

    private static ProductDocument Document(
        Guid tenantId,
        Guid productId,
        int version,
        string slug) => new()
    {
        TenantId = tenantId,
        ProductId = productId,
        Slug = slug,
        Name = $"Product {version}",
        IsActive = true,
        IsOnlineVisible = true,
        CreatedAtUtc = DateTime.UtcNow,
        SyncedAtUtc = DateTime.UtcNow
    };

    private sealed class GenerationState
    {
        private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);

        public GenerationState()
        {
            Cache.Setup(cache => cache.GetInvalidationGenerationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string key, string _, CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ThrowReads)
                    {
                        throw new InvalidOperationException("Redis unavailable");
                    }

                    return Task.FromResult<long?>(_generations.GetValueOrDefault(key));
                });
            Cache.Setup(cache => cache.AdvanceInvalidationGenerationAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _generations.AddOrUpdate(key, 1, (_, current) => current + 1);
                    return Task.CompletedTask;
                });
        }

        public Mock<IRedisJsonCacheService> Cache { get; } = new(MockBehavior.Loose);

        public bool ThrowReads { get; init; }

        public void Advance(Guid tenantId)
        {
            var key = CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey("b1-tests", tenantId);
            _generations.AddOrUpdate(key, 1, (_, current) => current + 1);
        }
    }

    private sealed class CountingRepository : ProductReadRepository
    {
        private readonly ProductDocument? _document;

        public CountingRepository(ProductDocument? document, IProductLookupValidationCache cache)
            : base(
                new MongoDbContext(
                    Options.Create(new MongoDbSettings
                    {
                        ConnectionString = "mongodb://127.0.0.1:27017",
                        DatabaseName = "b1-tests"
                    }),
                    NullLogger<MongoDbContext>.Instance),
                NullLogger<ProductReadRepository>.Instance,
                cache)
        {
            _document = document;
        }

        public int IdLoads { get; private set; }

        public int SlugLoads { get; private set; }

        public bool LastOnlineVisibleOnly { get; private set; }

        protected override Task<ProductDocument?> LoadByProductIdFromMongoAsync(
            Guid tenantId,
            Guid productId,
            CancellationToken cancellationToken)
        {
            IdLoads++;
            return Task.FromResult(_document);
        }

        protected override Task<ProductDocument?> LoadBySlugFromMongoAsync(
            Guid tenantId,
            string slug,
            bool onlineVisibleOnly,
            CancellationToken cancellationToken)
        {
            SlugLoads++;
            LastOnlineVisibleOnly = onlineVisibleOnly;
            return Task.FromResult(_document);
        }
    }
}
