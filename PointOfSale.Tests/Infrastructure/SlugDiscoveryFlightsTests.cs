using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.Tests.Infrastructure;

public sealed class SlugDiscoveryFlightsTests
{
    [Fact]
    public async Task HundredFollowers_UseOneFactory_EvenIfFollowerCancels()
    {
        var flights = new SlugDiscoveryFlights();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<SlugResolutionDto?> Discover(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return null;
        }

        var owner = flights.RunAsync("tenant:missing", Discover, CancellationToken.None);
        await entered.Task;
        using var cancel = new CancellationTokenSource();
        var canceledFollower = flights.RunAsync("tenant:missing", Discover, cancel.Token);
        var followers = Enumerable.Range(0, 99)
            .Select(_ => flights.RunAsync("tenant:missing", Discover, CancellationToken.None))
            .ToArray();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledFollower);
        release.SetResult();
        Assert.Null(await owner);
        Assert.All(await Task.WhenAll(followers), Assert.Null);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedFactory_IsRemovedAndCanRetry()
    {
        var flights = new SlugDiscoveryFlights();
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => flights.RunAsync(
            "tenant:retry", _ =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException();
            }, CancellationToken.None));
        Assert.Null(await flights.RunAsync("tenant:retry", _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<SlugResolutionDto?>(null);
        }, CancellationToken.None));
        Assert.Equal(2, calls);
    }
}
