using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// WRITE service for Storefront Domain Management (DOMAIN-3.1-C).
///
/// Implements the complete command path:
///   CreateDomain, UpdateDomain, ArchiveDomain, RestoreDomain,
///   DeleteDomain (soft), PromotePrimaryDomain.
///
/// Architecture Law #18: No Direct DB Writes Outside Domain Repository.
/// All mutations flow through IDomainRepository. The service layer
/// orchestrates validation, state-transition rules, audit entries,
/// events, and transactions — but never touches DbContext directly.
///
/// Architecture Law #11: No command may bypass optimistic concurrency.
/// The Version/xmin row-version token is enforced on every mutation.
///
/// Architecture Law #19: Events via IEventPublisher Only.
/// </summary>
public interface IDomainWriteService
{
    /// <summary>
    /// DOMAIN-3.1-C Command 1: CreateDomain.
    /// POST /api/admin/storefront/domains
    /// </summary>
    Task<DomainMutationResponse> CreateDomainAsync(
        CreateDomainRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-3.1-C Command 2: UpdateDomain.
    /// PATCH /api/admin/storefront/domains/{id}
    /// </summary>
    Task<DomainMutationResponse> UpdateDomainAsync(
        Guid domainId,
        UpdateDomainRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-4.3.1 Command: StartDomainVerification.
    /// PUT /api/admin/storefront/domains/{id}/verification/start
    /// </summary>
    Task<DomainMutationResponse> StartDomainVerificationAsync(
        Guid domainId,
        StartDomainVerificationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-4.5 Command: VerifyNow.
    /// PUT /api/admin/storefront/domains/{id}/verification/verify
    /// </summary>
    Task<VerificationResult> VerifyDomainNowAsync(
        Guid domainId,
        VerifyDomainNowRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-4.5 Command: RetryVerification.
    /// PUT /api/admin/storefront/domains/{id}/verification/retry
    /// </summary>
    Task<VerificationResult> RetryDomainVerificationAsync(
        Guid domainId,
        RetryDomainVerificationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-4.5 Command: RefreshVerificationToken.
    /// PUT /api/admin/storefront/domains/{id}/verification/token
    /// </summary>
    Task<DomainMutationResponse> RefreshDomainVerificationTokenAsync(
        Guid domainId,
        RefreshDomainVerificationTokenRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-3.1-C Command 3: ArchiveDomain.
    /// PUT /api/admin/storefront/domains/{id}/archive
    /// </summary>
    Task<DomainMutationResponse> ArchiveDomainAsync(
        Guid domainId,
        ArchiveDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-3.1-C Command 4: RestoreDomain.
    /// PUT /api/admin/storefront/domains/{id}/restore
    /// </summary>
    Task<DomainMutationResponse> RestoreDomainAsync(
        Guid domainId,
        RestoreDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-3.1-C Command 5: DeleteDomain (soft-delete only).
    /// DELETE /api/admin/storefront/domains/{id}
    /// </summary>
    Task<DomainMutationResponse> DeleteDomainAsync(
        Guid domainId,
        DeleteDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DOMAIN-3.1-C Command 6: PromotePrimaryDomain.
    /// PUT /api/admin/storefront/domains/{id}/primary
    /// Transactional: atomically promotes the target and demotes the current primary.
    /// </summary>
    Task<DomainMutationResponse> PromotePrimaryDomainAsync(
        Guid domainId,
        PromotePrimaryDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
