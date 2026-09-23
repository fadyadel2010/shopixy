using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class DomainAuthorityCacheTests
{
    [Fact]
    public async Task ColdMissThenWarmHit_ExecutesAuthorityFactoryOnce()
    {
        var cache = CreateCache();
        var calls = 0;
        var tenantId = Guid.NewGuid();

        ValueTask<DomainResolutionResult> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(DomainResolutionResult.FoundTenantAuthority("store.example.com", tenantId));
        }

        var cold = await cache.GetOrCreateAsync("store.example.com", Factory);
        var warm = await cache.GetOrCreateAsync("store.example.com", Factory);

        Assert.Equal(tenantId, cold.TenantId);
        Assert.Equal(tenantId, warm.TenantId);
        Assert.True(warm.CacheAttempted);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentColdMiss_IsSingleFlightPerAuthority()
    {
        var cache = CreateCache();
        var calls = 0;

        async ValueTask<DomainResolutionResult> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, cancellationToken);
            return DomainResolutionResult.NotFound("missing.example.com");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => cache.GetOrCreateAsync("missing.example.com", Factory).AsTask()));

        Assert.All(results, result => Assert.Equal(DomainResolutionStatus.NotFound, result.Status));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Invalidation_PreventsOverlappingStaleFillFromSurviving()
    {
        var cache = CreateCache();
        var oldTenantId = Guid.NewGuid();
        var newTenantId = Guid.NewGuid();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleFill = cache.GetOrCreateAsync(
            "changed.example.com",
            async _ =>
            {
                factoryEntered.SetResult();
                await releaseFactory.Task;
                return DomainResolutionResult.FoundTenantAuthority("changed.example.com", oldTenantId);
            }).AsTask();

        await factoryEntered.Task;
        await cache.InvalidateAllAsync();
        releaseFactory.SetResult();
        Assert.Equal(oldTenantId, (await staleFill).TenantId);

        var current = await cache.GetOrCreateAsync(
            "changed.example.com",
            _ => ValueTask.FromResult(
                DomainResolutionResult.FoundTenantAuthority("changed.example.com", newTenantId)));

        Assert.Equal(newTenantId, current.TenantId);
    }

    [Fact]
    public async Task DifferentAuthorities_DoNotShareTenantResults()
    {
        var cache = CreateCache();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await cache.GetOrCreateAsync(
            "a.example.com",
            _ => ValueTask.FromResult(DomainResolutionResult.FoundTenantAuthority("a.example.com", tenantA)));
        await cache.GetOrCreateAsync(
            "b.example.com",
            _ => ValueTask.FromResult(DomainResolutionResult.FoundTenantAuthority("b.example.com", tenantB)));

        var resultA = await cache.GetAsync("a.example.com");
        var resultB = await cache.GetAsync("b.example.com");
        Assert.Equal(tenantA, resultA?.TenantId);
        Assert.Equal(tenantB, resultB?.TenantId);
    }

    [Fact]
    public async Task ExplicitTransaction_AdvancesGenerationAfterCommitNotAfterSave()
    {
        var cache = new CountingDomainCache();
        var state = new DomainAuthorityCacheInvalidationState();
        var saveInterceptor = new DomainAuthorityCacheInvalidationInterceptor(cache, state);
        var transactionInterceptor = new DomainAuthorityTransactionInvalidationInterceptor(cache, state);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(saveInterceptor, transactionInterceptor)
            .Options;
        await using var context = new ConstraintFreeDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Committed authority",
            Slug = "committed-authority",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        await context.SaveChangesAsync();
        Assert.Equal(1, cache.InvalidationCount);

        await transaction.CommitAsync();
        Assert.Equal(2, cache.InvalidationCount);
    }

    [Fact]
    public async Task SlugWriters_FenceBeforeSaveAndOnlyInvalidateAfterCommit()
    {
        var cache = new CountingDomainCache();
        var slug = new Mock<IRedisJsonCacheService>();
        var invalidations = 0;
        slug.Setup(c => c.AdvanceInvalidationGenerationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref invalidations))
            .Returns(Task.CompletedTask);
        var settings = Options.Create(new RedisSettings { InstanceName = "writer-test" });
        var state = new DomainAuthorityCacheInvalidationState();
        var save = new DomainAuthorityCacheInvalidationInterceptor(cache, state, slug.Object, settings);
        var transaction = new DomainAuthorityTransactionInvalidationInterceptor(cache, state, slug.Object, settings);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(save, transaction).Options;
        await using var db = new ConstraintFreeDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.CmsPages.Add(new CmsPage
        {
            Id = Guid.NewGuid(), PageGroupId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            Slug = "new-page", Title = "Page", Content = "text", Version = 1,
            SchemaVersion = 1, CreatedAt = DateTime.UtcNow
        });
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.SaveChangesAsync();
            Assert.Equal(1, invalidations);
            await tx.RollbackAsync();
        }
        Assert.Equal(1, invalidations);
        db.ChangeTracker.Clear();

        db.CmsPages.Add(new CmsPage
        {
            Id = Guid.NewGuid(), PageGroupId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            Slug = "published-page", Title = "Page", Content = "text", Version = 1,
            SchemaVersion = 1, CreatedAt = DateTime.UtcNow
        });
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.SaveChangesAsync();
            Assert.Equal(2, invalidations);
            await tx.CommitAsync();
        }
        Assert.Equal(3, invalidations);
        slug.Verify(c => c.AdvanceInvalidationGenerationAsync(
            "writer-test:authority:seo-slug:generation:v1", It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RelationalAuthority_ColdWarmNegativeAndTenantIsolation_HaveBoundedSqlCommands()
    {
        var cache = CreateCache();
        var commands = new AuthorityCommandCounter();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands)
            .Options;
        await using var context = new ConstraintFreeDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var customDomainId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Currencies\" (\"Code\", \"Name\", \"Symbol\", \"DecimalPlaces\") VALUES ({"USD"}, {"US Dollar"}, {"$"}, {2})");
        foreach (var (id, slug) in new[] { (tenantA, "cache-alpha"), (tenantB, "cache-beta") })
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Tenants\" (\"Id\", \"Name\", \"Slug\", \"Currency\", \"IsActive\", \"Status\", \"CreatedAtUtc\", \"BootstrapVersion\", \"UseBaileys\") VALUES ({id}, {slug}, {slug}, {"USD"}, {true}, {(int)TenantStatus.Active}, {DateTime.UtcNow}, {1}, {false})");
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Domains\" (\"Id\", \"TenantId\", \"HostName\", \"NormalizedHostName\", \"DomainType\", \"LifecycleState\", \"VerificationToken\", \"RedirectType\", \"CanonicalPolicy\", \"CreatedAtUtc\", \"IsDeleted\", \"DomainHistoryJson\", \"CertificateOperationFencingToken\") VALUES ({customDomainId}, {tenantB}, {"cache-beta.example.com"}, {"cache-beta.example.com"}, {(int)DomainType.Primary}, {(int)DomainLifecycleState.Verified}, {"test-token"}, {(int)RedirectType.None}, {(int)CanonicalPolicy.UseAsPrimary}, {DateTime.UtcNow}, {false}, {"[]"}, {0L})");

        var resolver = new PersistenceDomainResolver(
            Mock.Of<IDomainRepository>(),
            context,
            cache,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "http://{slug}.localhost:5174"
            }).Build(),
            NullLogger<PersistenceDomainResolver>.Instance);

        commands.Reset();
        Assert.Equal(tenantA, (await resolver.ResolveAsync("cache-alpha.localhost")).TenantId);
        Assert.Equal(1, commands.Count);
        Assert.Equal(tenantA, (await resolver.ResolveAsync("cache-alpha.localhost")).TenantId);
        Assert.Equal(1, commands.Count);

        Assert.Equal(tenantB, (await resolver.ResolveAsync("cache-beta.example.com")).TenantId);
        Assert.Equal(2, commands.Count);
        Assert.Equal(tenantB, (await resolver.ResolveAsync("cache-beta.example.com")).TenantId);
        Assert.Equal(2, commands.Count);

        Assert.Equal(DomainResolutionStatus.NotFound, (await resolver.ResolveAsync("absent.localhost")).Status);
        Assert.Equal(3, commands.Count);
        Assert.Equal(DomainResolutionStatus.NotFound, (await resolver.ResolveAsync("absent.localhost")).Status);
        Assert.Equal(3, commands.Count);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Tenants\" SET \"IsActive\" = {false} WHERE \"Id\" = {tenantB}");
        await cache.InvalidateAllAsync();
        commands.Reset();
        Assert.Equal(DomainResolutionStatus.NotFound, (await resolver.ResolveAsync("cache-beta.example.com")).Status);
        Assert.Equal(2, commands.Count); // Domain join and base-URL fallback, both fail closed.
        Assert.Equal(tenantA, (await resolver.ResolveAsync("cache-alpha.localhost")).TenantId);
        Assert.Equal(3, commands.Count);
    }

    private sealed class AuthorityCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private static RedisDomainResolutionCache CreateCache()
    {
        var settings = Options.Create(new RedisSettings
        {
            Enabled = true,
            InstanceName = "domain-cache-tests"
        });
        var connectionFactory = new RedisConnectionFactory(
            settings,
            NullLogger<RedisConnectionFactory>.Instance,
            connectionOverride: null);
        return new RedisDomainResolutionCache(
            new InMemoryOwnedCache(),
            connectionFactory,
            settings);
    }

    private sealed class InMemoryOwnedCache : IRedisJsonCacheService
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, string cacheType, TimeSpan? maxProjectionLag = null, CancellationToken cancellationToken = default)
            where T : class => Task.FromResult(Get<T>(key, generationKey: null));

        public Task<T?> GetOwnedAsync<T>(string key, string cacheType, string invalidationGenerationKey, TimeSpan? maxProjectionLag = null, CancellationToken cancellationToken = default)
            where T : class => Task.FromResult(Get<T>(key, invalidationGenerationKey));

        public Task SetAsync<T>(string key, string cacheType, T value, TimeSpan ttl, DateTime? projectionLastUpdatedAtUtc = null, DateTime? sourceEventOccurredAtUtc = null, int schemaVersion = 1, CancellationToken cancellationToken = default)
            where T : class
        {
            _entries[key] = new Entry(value, 0);
            return Task.CompletedTask;
        }

        public Task<T?> GetOrCreateAsync<T>(string key, string cacheType, TimeSpan ttl, Func<CancellationToken, Task<CachePopulationResult<T>>> factory, TimeSpan? maxProjectionLag = null, CancellationToken cancellationToken = default)
            where T : class => GetOrCreateOwnedAsync(key, cacheType, string.Empty, ttl, factory, maxProjectionLag, cancellationToken);

        public async Task<T?> GetOrCreateOwnedAsync<T>(string key, string cacheType, string invalidationGenerationKey, TimeSpan ttl, Func<CancellationToken, Task<CachePopulationResult<T>>> factory, TimeSpan? maxProjectionLag = null, CancellationToken cancellationToken = default)
            where T : class
        {
            var cached = Get<T>(key, invalidationGenerationKey);
            if (cached != null)
            {
                return cached;
            }

            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                cached = Get<T>(key, invalidationGenerationKey);
                if (cached != null)
                {
                    return cached;
                }

                var generation = Generation(invalidationGenerationKey);
                var population = await factory(cancellationToken);
                if (population.Value != null && !population.SkipCache && generation == Generation(invalidationGenerationKey))
                {
                    _entries[key] = new Entry(population.Value, generation);
                }

                return population.Value;
            }
            finally
            {
                gate.Release();
            }
        }

        public Task AdvanceInvalidationGenerationAsync(string invalidationGenerationKey, CancellationToken cancellationToken = default)
        {
            _generations.AddOrUpdate(invalidationGenerationKey, 1, (_, generation) => generation + 1);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            foreach (var key in keys)
            {
                _entries.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private T? Get<T>(string key, string? generationKey) where T : class
        {
            if (!_entries.TryGetValue(key, out var entry) ||
                entry.Generation != Generation(generationKey))
            {
                return null;
            }

            return entry.Value as T;
        }

        private long Generation(string? key) =>
            string.IsNullOrEmpty(key) ? 0 : _generations.GetValueOrDefault(key);

        private sealed record Entry(object Value, long Generation);
    }

    private sealed class CountingDomainCache : IDomainResolutionCache
    {
        public int InvalidationCount { get; private set; }

        public ValueTask<DomainResolutionResult?> GetAsync(string normalizedHost, CancellationToken cancellationToken = default) => default;
        public ValueTask SetAsync(string normalizedHost, DomainResolutionResult result, CancellationToken cancellationToken = default) => default;
        public ValueTask RemoveAsync(string normalizedHost, CancellationToken cancellationToken = default) => default;

        public ValueTask<DomainResolutionResult> GetOrCreateAsync(
            string normalizedHost,
            Func<CancellationToken, ValueTask<DomainResolutionResult>> factory,
            CancellationToken cancellationToken = default) => factory(cancellationToken);

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
        {
            InvalidationCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConstraintFreeDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties().Where(property =>
                             string.Equals(property.GetColumnName(), "xmin", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }
}
