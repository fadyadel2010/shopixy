using System.Collections.Concurrent;
using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Bounded process-local fallback when Redis cannot coordinate discovery.
/// Followers never access the owner's scoped DbContext and their cancellation
/// cannot cancel its in-progress SQL/Mongo work.
/// </summary>
public sealed class SlugDiscoveryFlights
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SlugResolutionDto?>> _flights = new();
    private const int MaxFlights = 1024;
    private int _activeFlights;

    public async Task<SlugResolutionDto?> RunAsync(
        string key, Func<CancellationToken, Task<SlugResolutionDto?>> discovery,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_flights.TryGetValue(key, out var existing))
            return await existing.Task.WaitAsync(cancellationToken);

        if (Interlocked.Increment(ref _activeFlights) > MaxFlights)
        {
            Interlocked.Decrement(ref _activeFlights);
            return await discovery(cancellationToken);
        }

        var mine = new TaskCompletionSource<SlugResolutionDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flight = _flights.GetOrAdd(key, mine);
        if (!ReferenceEquals(flight, mine))
        {
            Interlocked.Decrement(ref _activeFlights);
            return await flight.Task.WaitAsync(cancellationToken);
        }

        try
        {
            var result = await discovery(cancellationToken);
            mine.TrySetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            mine.TrySetException(ex);
            throw;
        }
        finally
        {
            _flights.TryRemove(new KeyValuePair<string, TaskCompletionSource<SlugResolutionDto?>>(key, mine));
            Interlocked.Decrement(ref _activeFlights);
        }
    }
}
