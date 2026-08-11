using PointOfSale.Core.Entities;
using PointOfSale.Core.Models;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Repository for Domain persistence lookups (DOMAIN-2C).
///
/// Responsibilities ONLY:
///   - Lookup by normalized host
///   - Lookup primary domain
///   - Lookup aliases
///   - Lookup redirect target
///
/// Returns persistence models (Domain entities). Never resolves Tenant.
/// Never resolves StoreConfig. This is the persistence boundary.
/// </summary>
public interface IDomainRepository
{
    /// <summary>
    /// Looks up a single Domain by its normalized host name.
    /// Uses the UQ_Domains_NormalizedHostName unique index.
    /// </summary>
    /// <param name="normalizedHostName">The normalized hostnames (lowercase, punycode).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The matching Domain, or null if not found.</returns>
    Task<Domain?> FindByNormalizedHostAsync(
        string normalizedHostName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the primary domain for a tenant.
    /// Uses the UQ_Domains_TenantId_Primary filtered unique index
    /// (DomainType = Primary AND LifecycleState = Primary AND IsDeleted = false).
    /// </summary>
    /// <param name="tenantId">The tenant to find the primary domain for.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The primary Domain, or null if none found.</returns>
    Task<Domain?> FindPrimaryDomainAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up all alias domains for a tenant.
    /// Uses IX_Domains_TenantId_LifecycleState composite index.
    /// </summary>
    /// <param name="tenantId">The tenant to find alias domains for.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>All alias Domains for the tenant.</returns>
    Task<IReadOnlyList<Domain>> FindAliasDomainsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the redirect target domain for a given domain ID.
    /// Resolves the RedirectTargetDomainId FK to the target Domain row.
    /// Uses IX_Domains_RedirectTargetDomainId index.
    /// </summary>
    /// <param name="domainId">The source Domain ID (the one with RedirectType != None).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The redirect target Domain, or null if no redirect is configured.</returns>
    Task<Domain?> FindRedirectTargetAsync(
        Guid domainId,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // DOMAIN-3.1-C: Write-path repository methods
    // =========================================================================

    /// <summary>
    /// Adds a new domain to the repository.
    /// </summary>
    Task AddAsync(Domain domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single Domain by ID, scoped to the tenant, including
    /// a tracking reference for concurrency. Returns null if the domain
    /// does not exist or does not belong to the tenant.
    /// </summary>
    Task<Domain?> FindByIdAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single Domain by ID — ignoring soft-delete filter —
    /// scoped to the tenant. Used for restore operations.
    /// </summary>
    Task<Domain?> FindByIdIncludingDeletedAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a normalized hostname is already in use
    /// by any domain (across all tenants and states, including deleted).
    /// </summary>
    Task<bool> ExistsByNormalizedHostAsync(
        string normalizedHostName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a normalized hostname is already in use
    /// by the given tenant (including deleted/archived domains).
    /// </summary>
    Task<bool> ExistsByTenantAndNormalizedHostAsync(
        Guid tenantId,
        string normalizedHostName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any domain with redirect dependents points to
    /// the specified domain as its redirect target.
    /// </summary>
    Task<bool> HasRedirectDependentsAsync(
        Guid domainId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a domain audit entry to the repository.
    /// </summary>
    Task AddAuditEntryAsync(
        DomainAuditEntry auditEntry,
        CancellationToken cancellationToken = default);
}
