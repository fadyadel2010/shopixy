using PointOfSale.Core.Models;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Resolves a normalized host to a Domain Resolution Result.
///
/// This is the HOST → DOMAIN resolution contract (DOMAIN-2C). The resolver
/// accepts a <see cref="NormalizedHost"/> string (already normalized by
/// <see cref="IHostNormalizer"/>) and returns a <see cref="DomainResolutionResult"/>.
///
/// In DOMAIN-2C, the resolver is backed by the Domain persistence layer
/// (IDomainRepository) — it queries the Domains table by NormalizedHostName.
/// The IRepository performs no tenant resolution, no EF Core queries for
/// Tenant, and no StoreConfig lookup. This is the persistence boundary.
///
/// Per DOMAIN-1.6 Section 3.9 and DOMAIN-1 Section 7.2:
///   - The resolver accepts ONLY the normalized host string.
///   - The resolver does NOT resolve tenants.
///   - The resolver does NOT expose EF entities or database models.
///   - The resolver does NOT expose persistence details.
///
/// Resolution pipeline (DOMAIN-2C):
///   Host → Normalize (DOMAIN-2A) → IDomainResolver → DomainResolutionResult → STOP
///
/// No Tenant.
/// </summary>
public interface IDomainResolver
{
    /// <summary>
    /// Attempts to resolve a normalized host to a domain.
    ///
    /// The caller passes the already-normalized host (lowercase, port-stripped,
    /// trailing-dot removed, punycode-normalized). The resolver returns a
    /// <see cref="DomainResolutionResult"/> indicating whether a domain was
    /// found and, if so, carries the minimum constitutional information required
    /// for downstream pipeline stages.
    ///
    /// In DOMAIN-2C this method queries the Domains table via the persistence-backed
    /// IDomainRepository, checking the domain's LifecycleState for servability.
    /// Returns NotFound when no servable domain match exists for the host.
    ///</summary>
    /// <param name="normalizedHost">The normalized host string from <see cref="IHostResolutionContext.NormalizedHost"/>.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="DomainResolutionResult"/> describing the resolution outcome.</returns>
    ValueTask<DomainResolutionResult> ResolveAsync(string? normalizedHost, CancellationToken cancellationToken = default);
}
