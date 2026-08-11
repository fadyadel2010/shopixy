using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Explicit no-op implementation of <see cref="IDomainResolutionCache"/>.
///
/// DOMAIN-2C uses the persistence-backed <see cref="IDomainResolver"/> path
/// without a cache provider. This implementation keeps the cache contract
/// available for the DOMAIN-1.6 cache constitution while making runtime
/// behavior explicit: every lookup is a miss and mutation methods perform no
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
}
