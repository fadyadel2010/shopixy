using Moq;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.Tests.Infrastructure;

public sealed class RedisConnectionRecoveryTests
{
    [Fact]
    public async Task Initial_failure_can_recover_without_caching_null_forever()
    {
        var calls = 0; var connection = new Mock<IConnectionMultiplexer>();
        var shared = connection.Object;
        using var holder = new RecoverableRedisConnection(() => Interlocked.Increment(ref calls) == 1 ? null : shared,
            TimeSpan.FromSeconds(1), TimeSpan.Zero);
        Assert.Null(await holder.GetAsync(default));
        Assert.Same(connection.Object, await holder.GetAsync(default));
        Assert.Same(connection.Object, await holder.GetAsync(default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Bounded_wait_retains_single_inflight_connection_and_late_success()
    {
        using var release = new ManualResetEventSlim(); var calls = 0; var connection = new Mock<IConnectionMultiplexer>();
        var shared = connection.Object;
        using var holder = new RecoverableRedisConnection(() => { Interlocked.Increment(ref calls); release.Wait(); return shared; },
            TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        Assert.Null(holder.Value);
        await Assert.ThrowsAsync<TimeoutException>(() => holder.GetAsync(default));
        release.Set();
        Assert.Same(connection.Object, await holder.GetAsync(default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_abandon_or_duplicate_connect()
    {
        using var release = new ManualResetEventSlim(); var connection = new Mock<IConnectionMultiplexer>(); var calls = 0;
        var shared = connection.Object;
        using var holder = new RecoverableRedisConnection(() => { Interlocked.Increment(ref calls); release.Wait(); return shared; }, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        using var ct = new CancellationTokenSource(25);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holder.GetAsync(ct.Token));
        release.Set(); Assert.Same(connection.Object, await holder.GetAsync(default)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Shutdown_disposes_late_result_instead_of_leaking_it()
    {
        using var release = new ManualResetEventSlim(); var connection = new Mock<IConnectionMultiplexer>();
        var shared = connection.Object;
        var holder = new RecoverableRedisConnection(() => { release.Wait(); return shared; }, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var waiting = holder.GetAsync(default); holder.Dispose(); release.Set();
        Assert.Null(await waiting); connection.Verify(c => c.Dispose(), Times.Once);
        Assert.Throws<ObjectDisposedException>(() => holder.Value);
    }
}
