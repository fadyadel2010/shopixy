using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ComprehensiveCacheRemediationTests
{
    [Fact]
    public void ProductListQueryHash_IsDeterministicVersionedAndIdentityComplete()
    {
        var categoryId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var query = new ProductListQuery
        {
            Page = int.MaxValue,
            PageSize = 50,
            CategoryId = categoryId,
            IsActive = false
        };

        var first = query.GetQueryHash();
        var second = new ProductListQuery
        {
            Page = int.MaxValue,
            PageSize = 50,
            CategoryId = categoryId,
            IsActive = false
        }.GetQueryHash();

        Assert.Equal(first, second);
        Assert.StartsWith("h2-", first, StringComparison.Ordinal);
        Assert.Equal(67, first.Length);
        Assert.NotEqual(first, new ProductListQuery
        {
            Page = int.MaxValue,
            PageSize = 50,
            CategoryId = categoryId,
            IsActive = true
        }.GetQueryHash());
        Assert.NotEqual(first, new ProductListQuery
        {
            Page = int.MaxValue - 1,
            PageSize = 50,
            CategoryId = categoryId,
            IsActive = false
        }.GetQueryHash());
    }

    [Fact]
    public void FilterHash_IsCultureIndependentAndPreservesExplicitFalseAndDecimalScale()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            var arabicCulture = CacheKeyFilterNormalizer.NormalizeProductFiltersHash(
                search: " Shoe ", inStock: false, minPrice: 12.345m, page: 1, pageSize: 20);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var englishCulture = CacheKeyFilterNormalizer.NormalizeProductFiltersHash(
                search: "shoe", inStock: false, minPrice: 12.345m, page: 1, pageSize: 20);
            var omittedStock = CacheKeyFilterNormalizer.NormalizeProductFiltersHash(
                search: "shoe", inStock: null, minPrice: 12.345m, page: 1, pageSize: 20);

            Assert.Equal(arabicCulture, englishCulture);
            Assert.NotEqual(englishCulture, omittedStock);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ProductListKey_UsesNewSchemaAndTenantScope()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string queryHash = "h2-test";

        var keyA = CacheKeyBuilder.ProductsListKey("shopixy", tenantA, queryHash);
        var keyB = CacheKeyBuilder.ProductsListKey("shopixy", tenantB, queryHash);

        Assert.Contains(":products:list:v2:", keyA, StringComparison.Ordinal);
        Assert.False(keyA.EndsWith(":v1", StringComparison.Ordinal));
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public async Task StorefrontProductInvalidation_UsesGenerationWithoutPatternScans()
    {
        var tenantId = Guid.NewGuid();
        var cache = new Mock<IRedisJsonCacheService>(MockBehavior.Strict);
        cache.Setup(service => service.AdvanceInvalidationGenerationAsync(
                CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey("shopixy", tenantId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var invalidator = new StorefrontProductCacheInvalidator(
            cache.Object,
            Options.Create(new RedisSettings { InstanceName = "shopixy" }));

        await invalidator.InvalidateProductAsync(tenantId, Guid.NewGuid(), "product-slug");

        cache.VerifyAll();
        cache.Verify(service => service.RemoveByPatternAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(service => service.RemoveManyAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RedisWrite_CommandFalse_RecordsFailureOutcomeWithoutSuccess()
    {
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var cache = CreateCache(connection, telemetry);

        await cache.SetAsync("tenant-sensitive-key", "storefront-product", new TestPayload(), TimeSpan.FromMinutes(1));

        Assert.Contains("attempted", outcomes);
        Assert.Contains("command_false", outcomes);
        Assert.DoesNotContain("command_succeeded", outcomes);
    }

    [Fact]
    public async Task RedisWrite_DirectSuccess_RecordsCommandSuccess()
    {
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var cache = CreateCache(connection, telemetry);

        await cache.SetAsync("tenant-sensitive-key", "storefront-product", new TestPayload(), TimeSpan.FromMinutes(1));

        Assert.Contains("attempted", outcomes);
        Assert.Contains("command_succeeded", outcomes);
        Assert.DoesNotContain("command_false", outcomes);
        Assert.DoesNotContain("failed", outcomes);
    }

    [Fact]
    public async Task RedisWrite_CommandException_RecordsFailureWithoutSuccess()
    {
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis unavailable"));
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var cache = CreateCache(connection, telemetry);

        await cache.SetAsync("tenant-sensitive-key", "storefront-product", new TestPayload(), TimeSpan.FromMinutes(1));

        Assert.Contains("attempted", outcomes);
        Assert.Contains("failed", outcomes);
        Assert.DoesNotContain("command_succeeded", outcomes);
    }

    [Fact]
    public async Task RedisWrite_CallerCancellation_IsPropagatedAndRecorded()
    {
        using var cancellation = new CancellationTokenSource();
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var cache = CreateCache(connection, telemetry);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.SetAsync(
                "tenant-sensitive-key",
                "storefront-product",
                new TestPayload(),
                TimeSpan.FromMinutes(1),
                cancellationToken: cancellation.Token));

        Assert.Contains("attempted", outcomes);
        Assert.Contains("cancelled", outcomes);
        Assert.DoesNotContain("failed", outcomes);
        Assert.DoesNotContain("command_succeeded", outcomes);
    }

    [Fact]
    public async Task RedisWrite_GenerationAdvancedBeforePopulation_FencesStaleValue()
    {
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) =>
                Task.FromResult(values.Length == 3
                    ? RedisResult.Create(0)
                    : RedisResult.Create(new RedisValue[] { "0", RedisValue.Null })));
        database.Setup(redis => redis.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"0");
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var locks = new Mock<IDistributedLockService>(MockBehavior.Strict);
        locks.Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        locks.Setup(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var cache = CreateCache(connection, telemetry, locks.Object);

        var result = await cache.GetOrCreateOwnedAsync(
            $"product-list-{Guid.NewGuid():N}",
            "storefront-product",
            "product-generation",
            TimeSpan.FromMinutes(1),
            _ => Task.FromResult(CachePopulationResult<TestPayload>.FromValue(new TestPayload())));

        Assert.NotNull(result);
        Assert.Contains("attempted", outcomes);
        Assert.Contains("script_executed", outcomes);
        Assert.Contains("fenced", outcomes);
        Assert.DoesNotContain("command_succeeded", outcomes);
        locks.VerifyAll();
    }

    [Fact]
    public async Task RedisWrite_GenerationOwnedSuccess_RequiresAtomicScriptSuccess()
    {
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) =>
                Task.FromResult(values.Length == 3
                    ? RedisResult.Create(1)
                    : RedisResult.Create(new RedisValue[] { "0", RedisValue.Null })));
        database.Setup(redis => redis.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"0");
        var telemetry = new RedisPerformanceTelemetry();
        using var listener = CaptureWriteOutcomes(telemetry, out var outcomes);
        using var connection = CreateConnection(database.Object);
        var locks = new Mock<IDistributedLockService>(MockBehavior.Strict);
        locks.Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        locks.Setup(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var cache = CreateCache(connection, telemetry, locks.Object);

        var result = await cache.GetOrCreateOwnedAsync(
            $"product-list-{Guid.NewGuid():N}",
            "storefront-product",
            "product-generation",
            TimeSpan.FromMinutes(1),
            _ => Task.FromResult(CachePopulationResult<TestPayload>.FromValue(new TestPayload())));

        Assert.NotNull(result);
        Assert.Contains("attempted", outcomes);
        Assert.Contains("script_executed", outcomes);
        Assert.Contains("command_succeeded", outcomes);
        Assert.DoesNotContain("fenced", outcomes);
        Assert.DoesNotContain("failed", outcomes);
        locks.VerifyAll();
    }

    private static RedisConnectionFactory CreateConnection(IDatabase database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(redis => redis.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database);
        return new RedisConnectionFactory(
            Options.Create(new RedisSettings { Enabled = true, InstanceName = "cache-remediation-tests" }),
            NullLogger<RedisConnectionFactory>.Instance,
            multiplexer.Object);
    }

    private static RedisJsonCacheService CreateCache(
        RedisConnectionFactory connection,
        RedisPerformanceTelemetry telemetry,
        IDistributedLockService? locks = null)
    {
        return new RedisJsonCacheService(
            connection,
            locks ?? Mock.Of<IDistributedLockService>(),
            Options.Create(new RedisSettings { Enabled = true, InstanceName = "cache-remediation-tests" }),
            telemetry,
            NullLogger<RedisJsonCacheService>.Instance);
    }

    private static MeterListener CaptureWriteOutcomes(
        RedisPerformanceTelemetry telemetry,
        out ConcurrentQueue<string> outcomes)
    {
        var captured = new ConcurrentQueue<string>();
        outcomes = captured;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (ReferenceEquals(instrument, telemetry.CacheWriteOutcomes))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome" && tag.Value is string outcome)
                {
                    captured.Enqueue(outcome);
                }
            }
        });
        listener.Start();
        return listener;
    }

    private sealed class TestPayload
    {
        public bool Present { get; init; } = true;
    }
}
