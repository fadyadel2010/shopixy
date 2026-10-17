using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Admin;
using StackExchange.Redis;

namespace PointOfSale.Tests.Infrastructure;

public sealed class Phase5OwnedCacheTests
{
    [Theory]
    [InlineData("corrupt")]
    [InlineData("-1")]
    public async Task InvalidGeneration_CannotAdmitAnOldEnvelope(string generation)
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(new RedisValue[]
            {
                generation, "{\"payload\":{\"value\":\"stale\"},\"invalidationGeneration\":" + (generation == "-1" ? "-1" : "0") + "}"
            }));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        using var connection = new RedisConnectionFactory(Options.Create(new RedisSettings { Enabled = true }),
            NullLogger<RedisConnectionFactory>.Instance, multiplexer.Object);
        var cache = Create(connection);
        Assert.Null(await cache.GetOwnedAsync<Payload>("phase5-key", "test", "phase5-generation"));
    }

    [ConfiguredAdminIntegrationFact]
    public async Task ExistingRedis_AtomicFenceRejectsLateFillAndOldEnvelope_OnlyOwnedKeys()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
            .AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
        var settings = configuration.GetSection("Redis").Get<RedisSettings>()!;
        Assert.True(settings.Enabled);
        using var connection = new RedisConnectionFactory(Options.Create(settings), NullLogger<RedisConnectionFactory>.Instance);
        var database = connection.GetDatabase();
        Assert.NotNull(database);
        var prefix = $"phase5-verification:{Guid.NewGuid():N}";
        var key = prefix + ":value";
        var generation = prefix + ":generation";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = Create(connection);
        try
        {
            var late = cache.GetOrCreateOwnedWithStatusAsync(key, "test", generation, TimeSpan.FromMinutes(1), async _ =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return CachePopulationResult<Payload>.FromValue(new("old"));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.AdvanceInvalidationGenerationAsync(generation)));
            release.SetResult();
            Assert.Equal("old", (await late).Value!.Value); // The already-running read may linearize before invalidation.
            Assert.False(await database!.KeyExistsAsync(key)); // But its result must never survive in cache.
            Assert.Equal(8, await cache.GetInvalidationGenerationAsync(generation, "test"));
            var fresh = await cache.GetOrCreateOwnedWithStatusAsync(key, "test", generation, TimeSpan.FromMinutes(1),
                _ => Task.FromResult(CachePopulationResult<Payload>.FromValue(new("current"))));
            Assert.True(fresh.Refreshed);
            var hit = await cache.GetOrCreateOwnedWithStatusAsync<Payload>(key, "test", generation, TimeSpan.FromMinutes(1),
                _ => throw new InvalidOperationException("A warm accepted entry must not repopulate"));
            Assert.False(hit.Refreshed);
            Assert.Equal("current", hit.Value!.Value);
            await cache.AdvanceInvalidationGenerationAsync(generation);
            Assert.Null(await cache.GetOwnedAsync<Payload>(key, "test", generation));
        }
        finally
        {
            release.TrySetResult();
            await database!.KeyDeleteAsync(new RedisKey[] {key, generation});
            Assert.False(await database.KeyExistsAsync(key));
            Assert.False(await database.KeyExistsAsync(generation));
        }
    }

    [Fact]
    public async Task RequiredInvalidation_UnavailableEnabledRedisThrows_DisabledRedisIsExplicitlyOptional()
    {
        foreach (var enabled in new[] {true, false})
        {
            using var connection = new RedisConnectionFactory(Options.Create(new RedisSettings {Enabled = enabled}),
                NullLogger<RedisConnectionFactory>.Instance, null);
            var cache = Create(connection);
            if (enabled) await Assert.ThrowsAsync<RedisCacheInvalidationException>(() => cache.AdvanceInvalidationGenerationAsync("owned-generation"));
            else await cache.AdvanceInvalidationGenerationAsync("owned-generation");
        }
    }

    private static RedisJsonCacheService Create(RedisConnectionFactory connection)
    {
        var locks = new Mock<IDistributedLockService>();
        locks.Setup(l => l.TryAcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        locks.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        return new(connection, locks.Object, Options.Create(new RedisSettings {Enabled = true, InstanceName = "phase5"}),
            new RedisPerformanceTelemetry(), NullLogger<RedisJsonCacheService>.Instance);
    }

    public sealed record Payload(string Value);
}
