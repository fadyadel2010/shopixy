using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.Tests.Tenancy;

public sealed class KeyedSingleFlightTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task ConcurrentMisses_ShareOneLoad(int concurrency)
    {
        var loads = 0;
        var key = $"single-flight-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ =>
            KeyedSingleFlight.GetOrAddAsync(
                key,
                _ => Task.FromResult((false, string.Empty)),
                async cancellationToken =>
                {
                    Interlocked.Increment(ref loads);
                    await Task.Delay(20, cancellationToken);
                    return "authoritative";
                })));

        Assert.All(results, result => Assert.Equal("authoritative", result));
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task FastPathHit_DoesNotLoad()
    {
        var loads = 0;
        var key = $"single-flight-hit-{Guid.NewGuid():N}";

        var result = await KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((true, "cached")),
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult("loaded");
            });

        Assert.Equal("cached", result);
        Assert.Equal(0, loads);
    }

    [Fact]
    public async Task FailedLoad_IsEvictedAndRetried()
    {
        var loads = 0;
        var key = $"single-flight-retry-{Guid.NewGuid():N}";

        Task<string> CallAsync() => KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((false, string.Empty)),
            _ => Interlocked.Increment(ref loads) == 1
                ? Task.FromException<string>(new InvalidOperationException("transient"))
                : Task.FromResult("recovered"));

        await Assert.ThrowsAsync<InvalidOperationException>(CallAsync);
        Assert.Equal("recovered", await CallAsync());
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task CompletedLoad_IsEvictedForSequentialMiss()
    {
        var loads = 0;
        var key = $"single-flight-sequential-{Guid.NewGuid():N}";

        Task<int> CallAsync() => KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((false, 0)),
            _ => Task.FromResult(Interlocked.Increment(ref loads)));

        Assert.Equal(1, await CallAsync());
        Assert.Equal(2, await CallAsync());
    }

    [Fact]
    public async Task CancelledFollower_DoesNotCancelSharedLoad()
    {
        var loads = 0;
        var key = $"single-flight-cancellation-{Guid.NewGuid():N}";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var leader = KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((false, string.Empty)),
            async _ =>
            {
                Interlocked.Increment(ref loads);
                started.SetResult();
                await release.Task;
                return "authoritative";
            });
        await started.Task;

        using var cancellation = new CancellationTokenSource();
        var cancelledFollower = KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((false, string.Empty)),
            _ => Task.FromResult("duplicate"),
            cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledFollower);

        var remainingFollower = KeyedSingleFlight.GetOrAddAsync(
            key,
            _ => Task.FromResult((false, string.Empty)),
            _ => Task.FromResult("duplicate"));
        release.SetResult();

        Assert.Equal("authoritative", await leader);
        Assert.Equal("authoritative", await remainingFollower);
        Assert.Equal(1, loads);
    }
}
