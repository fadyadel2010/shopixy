using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models;

/// <summary>
/// Minimum runtime-only representation of a Host → Domain resolution outcome.
///
/// This is the output contract of <see cref="IDomainResolver"/> (DOMAIN-2C).
/// It must NOT depend on EF Core, <c>ApplicationDbContext</c>, repositories,
/// database entities, or future migrations. It is a pure runtime abstraction.
///
/// The abstraction contains only the minimum constitutional information
/// required for downstream pipeline stages (TenantResolutionMiddleware,
/// Host Resolution Middleware extensions). No persistence details, no EF
/// entities, no database models are exposed.
///
/// Resolution pipeline (DOMAIN-2C):
///   Host → Normalize → IDomainResolver → DomainResolutionResult → STOP
///   No Tenant.
/// </summary>
public sealed class DomainResolutionResult
{
    /// <summary>
    /// The normalized host that was submitted to the resolver.
    /// This is the same value stored in <see cref="IHostResolutionContext.NormalizedHost"/>.
    /// May be <c>null</c> if no host header was present.
    /// </summary>
    public string? NormalizedHost { get; init; }

    /// <summary>
    /// The outcome of the resolution attempt.
    /// </summary>
    public DomainResolutionStatus Status { get; init; }

    /// <summary>
    /// Indicates whether the resolver consulted the cache during this call.
    /// In DOMAIN-2C, this is <c>false</c> — no cache implementation exists
    /// in the persistence-backed resolver. A future cache layer (DOMAIN-3)
    /// will wire this through.
    /// </summary>
    public bool CacheAttempted { get; init; }

    /// <summary>
    /// The resolved Domain's primary key.
    /// Present only when <see cref="Status"/> is <see cref="DomainResolutionStatus.Found"/>.
    /// This is a runtime-only identifier — it does NOT expose the EF entity.
    /// Downstream middleware may use it for diagnostics, audit correlation, or
    /// future domain-scoped runtime work without touching EF entities directly.
    /// </summary>
    public Guid? DomainId { get; init; }

    /// <summary>
    /// The resolved Domain's TenantId.
    /// Present only when <see cref="Status"/> is <see cref="DomainResolutionStatus.Found"/>.
    /// This allows downstream middleware to set the tenant context without an
    /// additional database query, improving pipeline performance.
    ///
    /// NOTE: This is a resolved identifier carried in the result contract,
    /// NOT a Tenant resolution operation. The resolver itself does NOT perform
    /// tenant resolution — it only passes through the TenantId that is already
    /// stored on the Domain entity.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// The resolved Domain's redirect type, if any.
    /// Present when the Domain has a redirect configured (RedirectType != None).
    /// Allows the Host Resolution Middleware to short-circuit to redirect
    /// before StoreConfig/Theme/CMS resolution.
    /// </summary>
    public RedirectType? RedirectType { get; init; }

    /// <summary>
    /// The resolved Domain's redirect target hostname, if any.
    /// Present when RedirectType is not None. This is the NormalizedHostName
    /// of the target Domain, enabling the middleware to issue the redirect
    /// without an additional database lookup.
    /// </summary>
    public string? RedirectTargetNormalizedHost { get; init; }

    /// <summary>
    /// Creates a result indicating the host was not found in the domain registry.
    /// This is the default for a miss — the resolver queried the Domains table
    /// and found no servable domain for the given host.
    /// </summary>
    /// <param name="normalizedHost">The host that was submitted.</param>
    /// <returns>A result with <see cref="DomainResolutionStatus.NotFound"/>.</returns>
    public static DomainResolutionResult NotFound(string? normalizedHost) => new()
    {
        NormalizedHost = normalizedHost,
        Status = DomainResolutionStatus.NotFound,
        CacheAttempted = false
    };

    /// <summary>
    /// Creates a result indicating a domain was found in the registry.
    /// Used by the persistence-backed resolver (DOMAIN-2C) when a matching
    /// Domain row is retrieved from the repository.
    /// </summary>
    /// <param name="normalizedHost">The host that was submitted.</param>
    /// <param name="domainId">The resolved Domain's primary key.</param>
    /// <param name="tenantId">The resolved Domain's TenantId.</param>
    /// <param name="redirectType">The redirect type, if any (pass <c>null</c> for no redirect).</param>
    /// <param name="redirectTargetNormalizedHost">The redirect target's normalized host, if any.</param>
    /// <param name="cacheAttempted">Whether the cache was consulted.</param>
    /// <returns>A result with <see cref="DomainResolutionStatus.Found"/>.</returns>
    public static DomainResolutionResult Found(
        string? normalizedHost,
        Guid? domainId,
        Guid tenantId,
        RedirectType? redirectType = null,
        string? redirectTargetNormalizedHost = null,
        bool cacheAttempted = false) => new()
    {
        NormalizedHost = normalizedHost,
        Status = DomainResolutionStatus.Found,
        CacheAttempted = cacheAttempted,
        DomainId = domainId,
        TenantId = tenantId,
        RedirectType = redirectType,
        RedirectTargetNormalizedHost = redirectTargetNormalizedHost
    };

    /// <summary>
    /// Creates a found result for the canonical slug/base-url authority where
    /// no custom Domain row exists.
    /// </summary>
    public static DomainResolutionResult FoundTenantAuthority(
        string normalizedHost,
        Guid tenantId,
        bool cacheAttempted = false) => new()
    {
        NormalizedHost = normalizedHost,
        Status = DomainResolutionStatus.Found,
        CacheAttempted = cacheAttempted,
        TenantId = tenantId
    };
}
