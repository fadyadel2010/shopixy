using PointOfSale.Core.Models;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Cache abstraction for Domain Resolution lookups (DOMAIN-2C / DOMAIN-1.6).
///
/// This interface defines ONLY the contract required by DOMAIN-1.6 Section 13
/// (Cache Constitution) for host → domain resolution caching. No implementation
/// is provided — no Redis, no MemoryCache, no caching logic. The concrete cache
/// implementation will be introduced by a future DOMAIN-3 wave when the Domain
/// cache provider is activated.
///
/// <para>
/// The cache key is the <c>NormalizedHostName</c> (DOMAIN-1.5 CPD-8).
/// The cache value is a <see cref="DomainResolutionResult"/> representing
/// the resolution outcome, including both hits and misses (to cache negative
/// lookups during high-traffic scenarios).
/// </para>
///
/// <para>
/// Per DOMAIN-1.6 Section 13 (Domain Cache Constitution, lines 13):
/// TTL for Host Resolution cache: 10 minutes. Invalidation on domain state
/// transition events (<c>DomainVerified</c>, <c>DomainDeleted</c>,
/// <c>DomainPromotedToPrimary</c>).
/// </para>
/// </summary>
public interface IDomainResolutionCache
{
    /// <summary>
    /// Attempts to retrieve a cached domain resolution result for the
    /// given normalized host key.
    /// </summary>
    /// <param name="normalizedHost">The normalized host key (lowercase, port-stripped).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// The cached <see cref="DomainResolutionResult"/> if present, or <c>null</c>
    /// if the key is not found (cache miss). The current no-op implementation
    /// returns <c>null</c> because caching is explicitly disabled.
    /// </returns>
    ValueTask<DomainResolutionResult?> GetAsync(string normalizedHost, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a domain resolution result in the cache under the given
    /// normalized host key.
    /// </summary>
    /// <param name="normalizedHost">The normalized host key.</param>
    /// <param name="result">The resolution result to cache (may be a miss result).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    ValueTask SetAsync(string normalizedHost, DomainResolutionResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached entry for the given normalized host key.
    /// Called on domain state transitions that invalidate cached resolution
    /// (DOMAIN-1 Section 14: <c>DomainVerified</c>, <c>DomainDeleted</c>,
    /// <c>DomainPromotedToPrimary</c>).
    /// </summary>
    /// <param name="normalizedHost">The normalized host key to remove.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    ValueTask RemoveAsync(string normalizedHost, CancellationToken cancellationToken = default);
}
