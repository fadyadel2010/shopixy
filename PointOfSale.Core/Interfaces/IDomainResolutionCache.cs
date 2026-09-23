using PointOfSale.Core.Models;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Cache abstraction for Domain Resolution lookups (DOMAIN-2C / DOMAIN-1.6).
///
/// The production implementation is shared across application instances and
/// generation-fences cache fills against concurrent authority mutations.
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

    /// <summary>
    /// Returns a cached result or invokes the persistence factory under a
    /// per-authority single-flight boundary.
    /// </summary>
    ValueTask<DomainResolutionResult> GetOrCreateAsync(
        string normalizedHost,
        Func<CancellationToken, ValueTask<DomainResolutionResult>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the shared authority generation. Existing entries become
    /// unusable immediately on every process, and in-flight stale fills cannot
    /// publish into the new generation.
    /// </summary>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);
}
