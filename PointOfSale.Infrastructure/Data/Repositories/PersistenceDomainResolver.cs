using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public const string MeterName = "PointOfSale.Domain.Resolution";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> AuthoritySqlLookups = Meter.CreateCounter<long>(
        "domain.authority.sql_lookups", "count", "PostgreSQL authority lookup stages reached on cache misses");

    private readonly IDomainRepository _repository;
    private readonly ApplicationDbContext? _dbContext;
    private readonly IDomainResolutionCache _cache;
    private readonly string? _storefrontUrlTemplate;
    private readonly ILogger<PersistenceDomainResolver> _logger;

    [ActivatorUtilitiesConstructor]
    public PersistenceDomainResolver(
        IDomainRepository repository,
        ApplicationDbContext dbContext,
        IDomainResolutionCache cache,
        IConfiguration configuration,
        ILogger<PersistenceDomainResolver> logger)
    {
        _repository = repository;
        _dbContext = dbContext;
        _cache = cache;
        _storefrontUrlTemplate = configuration["Storefront:UrlTemplate"];
        _logger = logger;
    }

    // Retained for focused repository tests. Production DI always selects the
    // cache-enabled constructor above.
    public PersistenceDomainResolver(
        IDomainRepository repository,
        ILogger<PersistenceDomainResolver> logger)
    {
        _repository = repository;
        _cache = new NoCache();
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

        return await _cache.GetOrCreateAsync(
            normalizedHost,
            ct => ResolveFromPersistenceAsync(normalizedHost, ct),
            cancellationToken);
    }

    private async ValueTask<DomainResolutionResult> ResolveFromPersistenceAsync(
        string normalizedHost,
        CancellationToken cancellationToken)
    {
        if (_dbContext != null && TryExtractTenantSlug(normalizedHost, out var slug))
        {
            AuthoritySqlLookups.Add(1, new KeyValuePair<string, object?>("lookup", "canonical"));
            var tenantId = await _dbContext.Tenants
                .AsNoTracking()
                .Where(tenant =>
                    tenant.Slug == slug &&
                    tenant.IsActive &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null)
                .Select(tenant => (Guid?)tenant.Id)
                .SingleOrDefaultAsync(cancellationToken);

            return tenantId.HasValue
                ? DomainResolutionResult.FoundTenantAuthority(normalizedHost, tenantId.Value)
                : DomainResolutionResult.NotFound(normalizedHost);
        }

        if (_dbContext != null)
        {
            AuthoritySqlLookups.Add(1, new KeyValuePair<string, object?>("lookup", "custom_domain"));
            var authority = await (
                    from domainRow in _dbContext.Domains.IgnoreQueryFilters().AsNoTracking()
                    join tenant in _dbContext.Tenants.AsNoTracking()
                        on domainRow.TenantId equals tenant.Id
                    where !domainRow.IsDeleted &&
                          domainRow.NormalizedHostName == normalizedHost &&
                          (domainRow.LifecycleState == DomainLifecycleState.Verified ||
                           domainRow.LifecycleState == DomainLifecycleState.SslActive ||
                           domainRow.LifecycleState == DomainLifecycleState.Primary) &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null
                    select new
                    {
                        domainRow.Id,
                        domainRow.TenantId,
                        domainRow.RedirectType,
                        RedirectTarget = domainRow.RedirectTargetDomainId == null
                            ? null
                            : _dbContext.Domains
                                .IgnoreQueryFilters()
                                .Where(target =>
                                    !target.IsDeleted &&
                                    target.Id == domainRow.RedirectTargetDomainId &&
                                    target.TenantId == domainRow.TenantId)
                                .Select(target => target.NormalizedHostName)
                                .SingleOrDefault()
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (authority != null)
            {
                return DomainResolutionResult.Found(
                    normalizedHost,
                    authority.Id,
                    authority.TenantId,
                    authority.RedirectType == RedirectType.None ? null : authority.RedirectType,
                    authority.RedirectTarget);
            }

            var candidateOrigins = new[]
            {
                $"https://{normalizedHost}",
                $"https://{normalizedHost}/",
                $"http://{normalizedHost}",
                $"http://{normalizedHost}/"
            };
            AuthoritySqlLookups.Add(1, new KeyValuePair<string, object?>("lookup", "storefront_base_url"));
            var tenantIds = await _dbContext.Tenants
                .AsNoTracking()
                .Where(tenant =>
                    tenant.IsActive &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null &&
                    tenant.StorefrontBaseUrl != null &&
                    candidateOrigins.Contains(tenant.StorefrontBaseUrl.ToLower()))
                .Select(tenant => tenant.Id)
                .Take(2)
                .ToListAsync(cancellationToken);

            return tenantIds.Count == 1
                ? DomainResolutionResult.FoundTenantAuthority(normalizedHost, tenantIds[0])
                : DomainResolutionResult.NotFound(normalizedHost);
        }

        // Repository-only compatibility path for focused tests.
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

    private bool TryExtractTenantSlug(string normalizedHost, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(_storefrontUrlTemplate) ||
            !_storefrontUrlTemplate.Contains("{slug}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string marker = "__tenant_slug__";
        var markerTemplate = _storefrontUrlTemplate.Replace("{slug}", marker, StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(markerTemplate, UriKind.Absolute, out var templateUri))
        {
            return false;
        }

        var markerIndex = templateUri.Host.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var prefix = templateUri.Host[..markerIndex];
        var suffix = templateUri.Host[(markerIndex + marker.Length)..];
        if (!normalizedHost.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var length = normalizedHost.Length - prefix.Length - suffix.Length;
        if (length <= 0)
        {
            return false;
        }

        var candidate = normalizedHost.Substring(prefix.Length, length);
        if (candidate.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        slug = candidate.ToLowerInvariant();
        return true;
    }

    private sealed class NoCache : IDomainResolutionCache
    {
        public ValueTask<DomainResolutionResult?> GetAsync(string normalizedHost, CancellationToken cancellationToken = default) => default;
        public ValueTask SetAsync(string normalizedHost, DomainResolutionResult result, CancellationToken cancellationToken = default) => default;
        public ValueTask RemoveAsync(string normalizedHost, CancellationToken cancellationToken = default) => default;
        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default) => default;
        public ValueTask<DomainResolutionResult> GetOrCreateAsync(
            string normalizedHost,
            Func<CancellationToken, ValueTask<DomainResolutionResult>> factory,
            CancellationToken cancellationToken = default) => factory(cancellationToken);
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
