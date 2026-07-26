using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.Tests;

public sealed class DistributedLockServiceTests
{
    [Fact]
    public async Task ReleaseLockAsync_WhenOwnerDoesNotMatch_ReturnsFalse_AndNeverDeletesBlindly()
    {
        var databaseMock = new Mock<IDatabase>();
        var connectionMock = new Mock<IConnectionMultiplexer>();
        string? capturedScript = null;
        RedisKey[]? capturedKeys = null;
        RedisValue[]? capturedValues = null;

        databaseMock
            .Setup(database => database.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.None))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, _) =>
            {
                capturedScript = script;
                capturedKeys = keys;
                capturedValues = values;
            })
            .ReturnsAsync(RedisResult.Create(0));

        connectionMock
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var service = new DistributedLockService(
            connectionMock.Object,
            NullLogger<DistributedLockService>.Instance);

        var released = await service.ReleaseLockAsync("lock:checkout:test", "foreign-owner");

        Assert.False(released);
        Assert.NotNull(capturedScript);
        Assert.Contains("redis.call('get'", capturedScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redis.call('del'", capturedScript, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(capturedKeys);
        Assert.Single(capturedKeys!);
        Assert.Equal("lock:checkout:test", (string)capturedKeys[0]);
        Assert.NotNull(capturedValues);
        Assert.Single(capturedValues!);
        Assert.Equal("foreign-owner", (string)capturedValues[0]);

        databaseMock.Verify(database => database.KeyDeleteAsync(It.IsAny<RedisKey>(), CommandFlags.None), Times.Never);
        databaseMock.Verify(database => database.KeyDeleteAsync(It.IsAny<RedisKey[]>(), CommandFlags.None), Times.Never);
        databaseMock.Verify(database => database.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task RefreshLockAsync_WhenOwnerMatches_UsesCompareAndRefreshScript()
    {
        var databaseMock = new Mock<IDatabase>();
        var connectionMock = new Mock<IConnectionMultiplexer>();
        string? capturedScript = null;
        RedisKey[]? capturedKeys = null;
        RedisValue[]? capturedValues = null;
        var ttl = TimeSpan.FromMilliseconds(1500);

        databaseMock
            .Setup(database => database.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.None))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, _) =>
            {
                capturedScript = script;
                capturedKeys = keys;
                capturedValues = values;
            })
            .ReturnsAsync(RedisResult.Create(1));

        connectionMock
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var service = new DistributedLockService(
            connectionMock.Object,
            NullLogger<DistributedLockService>.Instance);

        var refreshed = await service.RefreshLockAsync("lock:checkout:test", "owner-token", ttl);

        Assert.True(refreshed.IsAcquired);
        Assert.NotNull(capturedScript);
        Assert.Contains("redis.call('get'", capturedScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redis.call('pexpire'", capturedScript, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(capturedKeys);
        Assert.Single(capturedKeys!);
        Assert.Equal("lock:checkout:test", (string)capturedKeys[0]);
        Assert.NotNull(capturedValues);
        Assert.Equal(2, capturedValues!.Length);
        Assert.Equal("owner-token", (string)capturedValues[0]);
        Assert.Equal("1500", (string)capturedValues[1]);
    }

    [Fact]
    public async Task TryAcquireLockAsync_WhenRedisThrows_ReturnsInfrastructureFailure()
    {
        var databaseMock = new Mock<IDatabase>();
        var connectionMock = new Mock<IConnectionMultiplexer>();

        databaseMock
            .Setup(database => database.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "boom"));

        connectionMock
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var service = new DistributedLockService(
            connectionMock.Object,
            NullLogger<DistributedLockService>.Instance);

        var result = await service.TryAcquireLockAsync("lock:checkout:test", "owner-token", TimeSpan.FromSeconds(30));

        Assert.True(result.IsInfrastructureFailure);
    }
}
