using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// READ-ONLY service for the Storefront Domain Management screens (DOMAIN-3.1-B).
///
/// Serves all 9 screens (D01-D09) with real backend DATA through a canonical
/// architecture: Service → EF Core → Domain Entities → DTO mapping.
///
/// D03 (AddDomainWizardPage) remains UI-only by design — no API call.
///
/// Tenant isolation is enforced by the ApplicationDbContext query filters
/// (see ApplicationDbContext.cs:954 — Domain filter excludes System/Development
/// from tenant scoping, but all other domain types are tenant-filtered).
///
/// Architecture boundary: The service returns DTOs (projection layer), NOT entities.
/// The controller stays thin — delegates entirely to the service.
/// </summary>
public interface IDomainReadService
{
    /// <summary>
    /// D04: Returns a paginated, filtered, sorted list of domains for the tenant.
    /// </summary>
    Task<DomainListPageDto> GetDomainListAsync(
        Guid tenantId,
        int page,
        int pageSize,
        string? search = null,
        DomainType? typeFilter = null,
        DomainLifecycleState? stateFilter = null,
        string? sortBy = null,
        string? sortDir = null,
        CancellationToken ct = default);

    /// <summary>
    /// D02: Returns detailed information for a single domain.
    /// </summary>
    Task<DomainDetailDto?> GetDomainDetailAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default);

    /// <summary>
    /// D01: Returns the dashboard summary: primary domain, counts, recent domains.
    /// </summary>
    Task<DomainDashboardSummaryDto> GetDashboardSummaryAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// D05: Returns DNS verification details for a single domain.
    /// </summary>
    Task<DomainVerificationDto?> GetDomainVerificationAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default);

    /// <summary>
    /// D06: Returns SSL certificate details for a single domain.
    /// </summary>
    Task<DomainSslDto?> GetDomainSslAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default);

    /// <summary>
    /// D07: Returns redirect rules sourced from this domain (where this domain
    /// is the redirect source). The redirect target host is resolved.
    /// Returns null if the domain does not exist or does not belong to the tenant.
    /// </summary>
    Task<IReadOnlyList<DomainRedirectDto>?> GetDomainRedirectsAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default);

    /// <summary>
    /// D08: Returns the paginated audit history for a single domain.
    /// </summary>
    Task<DomainHistoryPageDto> GetDomainHistoryAsync(
        Guid tenantId,
        Guid domainId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// D09: Returns recent activity entries across all tenant domains.
    /// </summary>
    Task<DomainActivitySummaryDto> GetActivitySummaryAsync(
        Guid tenantId,
        int limit,
        CancellationToken ct = default);
}
