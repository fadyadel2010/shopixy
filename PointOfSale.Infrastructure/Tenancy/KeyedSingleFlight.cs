using System.Collections.Concurrent;

namespace PointOfSale.Infrastructure.Tenancy;

/// <summary>
/// Coalesces concurrent same-key cache misses within one process.
/// </summary>
public static class KeyedSingleFlight
{
    private static class Holder<T>
    {
        public static readonly ConcurrentDictionary<string, Lazy<Task<T>>> InFlight =
            new(StringComparer.Ordinal);
    }

    public static Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<(bool Hit, T Value)>> checkAsync,
        Func<CancellationToken, Task<T>> loadAsync,
        CancellationToken cancellationToken = default)
    {
        return CoreAsync(key, checkAsync, loadAsync, cancellationToken);
    }

    private static async Task<T> CoreAsync<T>(
        string key,
        Func<CancellationToken, Task<(bool Hit, T Value)>> checkAsync,
        Func<CancellationToken, Task<T>> loadAsync,
        CancellationToken cancellationToken)
    {
        var (hit, value) = await checkAsync(cancellationToken).ConfigureAwait(false);
        if (hit)
        {
            return value;
        }

        var candidate = new Lazy<Task<T>>(
            () => ExecuteDetachedAsync(checkAsync, loadAsync),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var winner = Holder<T>.InFlight.GetOrAdd(key, candidate);
        var ownsLoad = ReferenceEquals(candidate, winner);
        var shared = winner.Value;

        try
        {
            return ownsLoad
                ? await shared.ConfigureAwait(false)
                : await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsLoad)
            {
                Holder<T>.InFlight.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, winner));
            }
        }
    }

    private static async Task<T> ExecuteDetachedAsync<T>(
        Func<CancellationToken, Task<(bool Hit, T Value)>> checkAsync,
        Func<CancellationToken, Task<T>> loadAsync)
    {
        var (hit, value) = await checkAsync(CancellationToken.None).ConfigureAwait(false);
        return hit
            ? value
            : await loadAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
