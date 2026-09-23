using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Explicit no-op implementation of <see cref="IDomainResolutionCache"/>.
///
/// Compatibility fallback for hosts that intentionally register domain
/// resolution without a cache provider. Production registers the Redis-backed
/// implementation; this type always misses and mutation methods perform no
/// storage work.
/// </summary>
public sealed class NoOpDomainResolutionCache : IDomainResolutionCache
{
    /// <inheritdoc />
    public ValueTask<DomainResolutionResult?> GetAsync(string normalizedHost, CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetAsync(string normalizedHost, DomainResolutionResult result, CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string normalizedHost, CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask<DomainResolutionResult> GetOrCreateAsync(
        string normalizedHost,
        Func<CancellationToken, ValueTask<DomainResolutionResult>> factory,
        CancellationToken cancellationToken = default)
    {
        return factory(cancellationToken);
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        return default;
    }
}
