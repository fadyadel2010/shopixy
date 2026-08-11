using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.Infrastructure.Data.Repositories;

/// <summary>
/// Persistence-backed implementation of <see cref="IDomainResolver"/> (DOMAIN-2C).
///
/// This resolver is the active DOMAIN-2C runtime resolver. It performs a real
/// lookup against the <c>Domains</c> table
/// via <see cref="IDomainRepository"/> — backed by EF Core and PostgreSQL.
///
/// Resolution pipeline (DOMAIN-2C):
///   NormalizedHost → IDomainRepository → Domain Entity → DomainResolutionResult → STOP
///   No Tenant Resolution. No StoreConfig. No Theme. No CMS.
///
/// The resolver returns a <see cref="DomainResolutionResult"/> containing:
///   - <c>DomainId</c> — the resolved Domain's primary key
///   - <c>TenantId</c> — the Domain's TenantId (pass-through, not resolved)
///   - <c>RedirectType</c> / <c>RedirectTargetNormalizedHost</c> — if the Domain
///     has a redirect configured
///
/// Grounded In:
///   - DOMAIN-1.6 Section 4: "Query the Domains table by NormalizedHostName"
///   - DOMAIN-1.5 CPD-5: System/Development exemption from tenant filter
///   - DOMAIN-1.6 Section 4 line 564: lifecycle state gate for serving
/// </summary>
public sealed class PersistenceDomainResolver : IDomainResolver
{
    private readonly IDomainRepository _repository;
    private readonly ILogger<PersistenceDomainResolver> _logger;

    public PersistenceDomainResolver(
        IDomainRepository repository,
        ILogger<PersistenceDomainResolver> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    /// Performs a real database lookup by NormalizedHostName.
    /// Returns NotFound if no domain matches, or if the matched domain's
    /// LifecycleState is not in the set of "servable" states
    /// (Verified, SslActive, Primary).
    public async ValueTask<DomainResolutionResult> ResolveAsync(
        string? normalizedHost,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            _logger.LogDebug(
                "PersistenceDomainResolver: normalizedHost is null or empty. Status={Status} CacheAttempted={CacheAttempted}",
                DomainResolutionStatus.NotFound,
                false);

            return DomainResolutionResult.NotFound(normalizedHost);
        }

        // Repository lookup by normalized host — single B-tree index seek.
        var domain = await _repository.FindByNormalizedHostAsync(normalizedHost, cancellationToken);

        if (domain == null)
        {
            _logger.LogDebug(
                "PersistenceDomainResolver: host={NormalizedHost} not found in Domains table. Status={Status} CacheAttempted={CacheAttempted}",
                normalizedHost,
                DomainResolutionStatus.NotFound,
                false);

            return DomainResolutionResult.NotFound(normalizedHost);
        }

        // Lifecycle state gate (DOMAIN-1.6 Section 4, Constitutional Runtime):
        // Only domains in Verified, SslActive, or Primary states are "found"
        // for resolution. Disabled/Archived states return NotFound so the
        // pipeline falls through to JWT/slug fallback.
        if (!IsServableState(domain.LifecycleState))
        {
            _logger.LogDebug(
                "PersistenceDomainResolver: domain={NormalizedHost} found but LifecycleState={LifecycleState} is not servable. Status={Status}",
                normalizedHost,
                domain.LifecycleState,
                DomainResolutionStatus.NotFound);

            return DomainResolutionResult.NotFound(normalizedHost);
        }

        // Build the redirect target host if this is a redirect domain.
        string? redirectTargetNormalizedHost = null;
        if (domain.RedirectType != RedirectType.None && domain.RedirectTargetDomainId.HasValue)
        {
            var redirectTarget = await _repository.FindRedirectTargetAsync(
                domain.Id, cancellationToken);

            if (redirectTarget != null)
            {
                redirectTargetNormalizedHost = redirectTarget.NormalizedHostName;
            }
        }

        _logger.LogDebug(
            "PersistenceDomainResolver: host={NormalizedHost} resolved. DomainId={DomainId} TenantId={TenantId} Status={Status} CacheAttempted={CacheAttempted} RedirectType={RedirectType}",
            normalizedHost,
            domain.Id,
            domain.TenantId,
            DomainResolutionStatus.Found,
            false,
            domain.RedirectType);

        return DomainResolutionResult.Found(
            normalizedHost: normalizedHost,
            domainId: domain.Id,
            tenantId: domain.TenantId,
            redirectType: domain.RedirectType != RedirectType.None
                ? domain.RedirectType
                : null,
            redirectTargetNormalizedHost: redirectTargetNormalizedHost,
            cacheAttempted: false);
    }

    /// <summary>
    /// Determines whether a LifecycleState is "servable" — i.e., the domain
    /// is in a state where it should be resolved for storefront traffic.
    ///
    /// Per DOMAIN-1.6 Section 4:
    ///   "If found and LifecycleState ∈ {Verified, SslActive, Primary}: set
    ///    HttpContext.Items[ResolvedDomain]"
    ///
    /// States NOT in this set (Draft, PendingVerification, SslPending,
    /// Disabled, Archived, Deleted) return NotFound — the pipeline falls
    /// through to JWT/slug-based tenant resolution.
    /// </summary>
    private static bool IsServableState(DomainLifecycleState state)
    {
        return state is DomainLifecycleState.Verified
            or DomainLifecycleState.SslActive
            or DomainLifecycleState.Primary;
    }
}
