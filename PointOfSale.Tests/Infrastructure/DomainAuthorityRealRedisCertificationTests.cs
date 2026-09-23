using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Models;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace PointOfSale.Tests.Infrastructure;

public sealed class DomainAuthorityRealRedisCertificationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "MainRuntimeCertification")]
    public async Task SeoNegative_RealRedis_CoalescesHundredAndFencesMutation()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(PointOfSale.API.Controllers.StorefrontCartController).Assembly, optional: true)
            .Build();
        var endpoint = config["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw SkipException.ForSkip("Requires the locally configured API Redis user-secret.");

        var instance = $"slug-cert-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();
        var settings = Options.Create(new RedisSettings
        {
            Enabled = true, ConnectionString = endpoint, InstanceName = instance
        });
        using var connection = new RedisConnectionFactory(settings, NullLogger<RedisConnectionFactory>.Instance);
        Assert.True(connection.IsConnected);
        var cache = new RedisJsonCacheService(connection,
            new DistributedLockService(connection, NullLogger<DistributedLockService>.Instance),
            settings, new RedisPerformanceTelemetry(), NullLogger<RedisJsonCacheService>.Instance);
        var generation = SeoSlugAuthorityKeys.Generation(instance);
        var key = SeoSlugAuthorityKeys.Negative(instance, tenantId, "absent");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        async Task<CachePopulationResult<NegativeMarker>> Discover(CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return CachePopulationResult<NegativeMarker>.FromValue(new NegativeMarker());
        }

        try
        {
            var owner = cache.GetOrCreateOwnedAsync(key, "seo-slug-negative", generation,
                TimeSpan.FromSeconds(3), Discover);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var followers = Enumerable.Range(0, 99).Select(_ =>
                cache.GetOrCreateOwnedAsync(key, "seo-slug-negative", generation,
                    TimeSpan.FromSeconds(3), Discover)).ToArray();
            release.SetResult();
            Assert.NotNull(await owner);
            Assert.All(await Task.WhenAll(followers), Assert.NotNull);
            Assert.Equal(1, executions);

            await cache.AdvanceInvalidationGenerationAsync(generation);
            Assert.Null(await cache.GetOwnedAsync<NegativeMarker>(key, "seo-slug-negative", generation));
            Assert.NotNull(await cache.GetOrCreateOwnedAsync(key, "seo-slug-negative", generation,
                TimeSpan.FromSeconds(3), Discover));
            Assert.Equal(2, executions);
            Assert.NotEqual(key, SeoSlugAuthorityKeys.Negative(instance, Guid.NewGuid(), "absent"));
            Assert.NotEqual(key, SeoSlugAuthorityKeys.Negative(instance, tenantId, "other"));
            Assert.NotEqual(key, SeoSlugAuthorityKeys.Negative(instance, tenantId, "Absent"));
        }
        finally
        {
            var db = connection.GetDatabase();
            if (db != null)
            {
                await db.KeyDeleteAsync(key);
                await db.KeyDeleteAsync(generation);
            }
        }
    }

    private sealed class NegativeMarker { public bool Missing { get; set; } = true; }

    [Fact]
    [Trait("Category", "MainRuntimeCertification")]
    public async Task IsolatedRealRedis_ProvesColdWarmSingleFlightInvalidationAndTenantIsolation()
    {
        // Read the same local secret that starts MAIN; never print it. The
        // unique key prefix and local SQLite database isolate this probe from
        // production tenant/domain rows and existing cache entries.
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(PointOfSale.API.Controllers.StorefrontCartController).Assembly, optional: true)
            .Build();
        var connectionString = config["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw SkipException.ForSkip("Requires the locally configured API Redis user-secret.");
        }

        var instance = $"main-cert-{Guid.NewGuid():N}";
        var settings = Options.Create(new RedisSettings
        {
            Enabled = true,
            ConnectionString = connectionString!,
            InstanceName = instance
        });
        using var factory = new RedisConnectionFactory(settings, NullLogger<RedisConnectionFactory>.Instance);
        Assert.True(factory.IsConnected, "The configured Redis endpoint must be connected for this runtime certification.");
        var cache = new RedisDomainResolutionCache(
            new RedisJsonCacheService(
                factory,
                new DistributedLockService(factory, NullLogger<DistributedLockService>.Instance),
                settings,
                new RedisPerformanceTelemetry(),
                NullLogger<RedisJsonCacheService>.Instance),
            factory,
            settings);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new QueryCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands)
            .Options;
        await using var context = new ConstraintFreeDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var custom = "cert-custom.example.test";
        var canonical = "cert-alpha.localhost";
        var missing = "cert-missing.localhost";
        var race = "cert-race.example.test";
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Currencies\" (\"Code\", \"Name\", \"Symbol\", \"DecimalPlaces\") VALUES ({"USD"}, {"US Dollar"}, {"$"}, {2})");
            foreach (var (id, slug) in new[] { (tenantA, "cert-alpha"), (tenantB, "cert-beta") })
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Tenants\" (\"Id\", \"Name\", \"Slug\", \"Currency\", \"IsActive\", \"Status\", \"CreatedAtUtc\", \"BootstrapVersion\", \"UseBaileys\") VALUES ({id}, {slug}, {slug}, {"USD"}, {true}, {(int)TenantStatus.Active}, {DateTime.UtcNow}, {1}, {false})");
            }
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Domains\" (\"Id\", \"TenantId\", \"HostName\", \"NormalizedHostName\", \"DomainType\", \"LifecycleState\", \"VerificationToken\", \"RedirectType\", \"CanonicalPolicy\", \"CreatedAtUtc\", \"IsDeleted\", \"DomainHistoryJson\", \"CertificateOperationFencingToken\") VALUES ({Guid.NewGuid()}, {tenantB}, {custom}, {custom}, {(int)DomainType.Primary}, {(int)DomainLifecycleState.Verified}, {"test-token"}, {(int)RedirectType.None}, {(int)CanonicalPolicy.UseAsPrimary}, {DateTime.UtcNow}, {false}, {"[]"}, {0L})");

            var resolver = new PersistenceDomainResolver(
                Mock.Of<IDomainRepository>(), context, cache,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storefront:UrlTemplate"] = "http://{slug}.localhost:5174"
                }).Build(),
                NullLogger<PersistenceDomainResolver>.Instance);

            commands.Reset();
            Assert.Equal(tenantA, (await resolver.ResolveAsync(canonical)).TenantId);
            Assert.Equal(1, commands.Count);
            Assert.Equal(tenantA, (await resolver.ResolveAsync(canonical)).TenantId);
            Assert.Equal(1, commands.Count);

            Assert.Equal(tenantB, (await resolver.ResolveAsync(custom)).TenantId);
            Assert.Equal(2, commands.Count);
            Assert.Equal(tenantB, (await resolver.ResolveAsync(custom)).TenantId);
            Assert.Equal(2, commands.Count);

            var misses = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => resolver.ResolveAsync(missing).AsTask()));
            Assert.All(misses, result => Assert.Equal(DomainResolutionStatus.NotFound, result.Status));
            Assert.Equal(3, commands.Count);
            Assert.Equal(DomainResolutionStatus.NotFound, (await resolver.ResolveAsync(missing)).Status);
            Assert.Equal(3, commands.Count);

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stale = cache.GetOrCreateAsync(race, async _ =>
            {
                entered.SetResult();
                await release.Task;
                return DomainResolutionResult.FoundTenantAuthority(race, tenantB);
            }).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cache.InvalidateAllAsync();
            release.SetResult();
            Assert.Equal(tenantB, (await stale).TenantId);
            Assert.Null(await cache.GetAsync(race));

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Tenants\" SET \"IsActive\" = {false} WHERE \"Id\" = {tenantB}");
            await cache.InvalidateAllAsync();
            commands.Reset();
            Assert.Equal(DomainResolutionStatus.NotFound, (await resolver.ResolveAsync(custom)).Status);
            Assert.Equal(2, commands.Count);
            Assert.Equal(tenantA, (await resolver.ResolveAsync(canonical)).TenantId);
            Assert.Equal(3, commands.Count);
            output.WriteLine("Real Redis: canonical cold/warm SQL 1/0; custom cold/warm 1/0; 16 negative cold requests SQL 1; invalidation rejected stale fill and inactive tenant; no cross-tenant result.");
        }
        finally
        {
            var db = factory.GetDatabase();
            if (db != null)
            {
                foreach (var host in new[] { canonical, custom, missing, race })
                {
                    await db.KeyDeleteAsync($"{instance}:authority:domain:v1:{host}");
                }
                await db.KeyDeleteAsync($"{instance}:authority:domain:generation:v1");
            }
        }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
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
