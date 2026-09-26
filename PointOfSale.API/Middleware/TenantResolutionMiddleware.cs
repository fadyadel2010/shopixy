using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PointOfSale.API.Attributes;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Resolves tenant context from trusted request metadata only.
/// Server-authoritative order:
/// 1. Authenticated tenant claim
/// 2. Resolved Domain (from DomainResolutionResult produced by HostResolutionMiddleware)
/// 3. Verified storefront/custom-domain host (server-controlled Host header only)
/// 4. Slug extraction from Storefront:UrlTemplate (fallback)
/// 
/// SECURITY: Origin and Referer headers are NEVER trusted as they are client-controlled
/// and can be spoofed by attackers to access other tenants' data.
/// 
/// Raw tenant headers are never trusted as an input source. The middleware strips
/// any inbound X-Tenant-Id header before downstream code runs, then re-adds the
/// header only after the tenant is resolved from a trusted source. This preserves
/// backward compatibility with legacy controller code without exposing client-
/// supplied tenant IDs to the request pipeline.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly string? _storefrontUrlTemplate;

    private const string SlugMarker = "__tenant_slug__";

    /// <summary>
    /// Header name preserved only as a compatibility output after secure resolution.
    /// </summary>
    public const string TenantIdHeaderName = "X-Tenant-Id";

    /// <summary>
    /// Paths that should bypass tenant resolution entirely.
    /// Only system-level or globally scoped endpoints belong here.
    /// </summary>
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/swagger",
        "/hangfire",
        "/health",
        "/api/super-admin",
        "/api/tenants",
        "/api/admin/finance",
        "/api/platform/register",
        "/api/platform/subdomain/check",
        "/api/platform/email/check",
        "/hubs/chat",
        "/uploads",
        "/api/affiliate/auth",
        "/api/public/affiliate",
"/api/public/feeds",
        "/api/affiliate",
        // Test harness entry-point: stripped from Production by TestingOnlyControllerFeatureProvider.
        // Must bypass tenant resolution because it has no token yet — it IS the token issuer.
        "/api/test-auth"
    };

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _storefrontUrlTemplate = configuration["Storefront:UrlTemplate"];
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, ApplicationDbContext dbContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var inboundTenantHeader = context.Request.Headers[TenantIdHeaderName].ToString();
        context.Request.Headers.Remove(TenantIdHeaderName);

        if (ShouldSkipTenantResolution(path))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        var allowsAnonymousTenant = endpoint?.Metadata.GetMetadata<AllowAnonymousTenantAttribute>() != null;

        var authenticatedTenant = await ResolveAuthenticatedTenantAsync(
            context,
            dbContext,
            inboundTenantHeader,
            context.RequestAborted);

        var requestTenant = await ResolveRequestTenantAsync(
            context,
            dbContext,
            authenticatedTenant,
            context.RequestAborted);

        var effectiveTenant = authenticatedTenant ?? requestTenant;
        var reliabilityTelemetry = context.RequestServices.GetService<CommerceReliabilityTelemetry>();

        if (!string.IsNullOrWhiteSpace(inboundTenantHeader) &&
            effectiveTenant != null &&
            !HeaderMatchesTenant(inboundTenantHeader, effectiveTenant))
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         context.User.FindFirstValue("sub") ??
                         "anonymous";

            _logger.LogWarning(
                "[SECURITY] Tenant spoofing attempt detected | TrustedTenantId: {TrustedTenantId} | HeaderTenant: {HeaderTenant} | Path: {Path} | UserId: {UserId}",
                effectiveTenant.Id,
                inboundTenantHeader,
                path,
                userId);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Tenant spoofing detected.",
                code = "TENANT_FORBIDDEN"
            });
            reliabilityTelemetry?.RecordTenantAccessViolation("header_spoofing", path);
            return;
        }

        if (authenticatedTenant != null &&
            requestTenant != null &&
            authenticatedTenant.Id != requestTenant.Id)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         context.User.FindFirstValue("sub") ??
                         "anonymous";

            _logger.LogWarning(
                "[SECURITY] Cross-tenant access attempt detected | AuthTenantId: {AuthTenantId} | RequestTenantId: {RequestTenantId} | Path: {Path} | UserId: {UserId}",
                authenticatedTenant.Id,
                requestTenant.Id,
                path,
                userId);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Tenant isolation violation",
                code = "TENANT_FORBIDDEN"
            });
            reliabilityTelemetry?.RecordTenantAccessViolation("cross_tenant_mismatch", path);
            return;
        }

        if (effectiveTenant == null)
        {
            if (allowsAnonymousTenant)
            {
                await _next(context);
                return;
            }

            _logger.LogWarning(
                "Request rejected: Tenant could not be resolved from a trusted source | Path: {Path} | IP: {IP}",
                path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Tenant context could not be resolved securely",
                code = "TENANT_REQUIRED",
                hint = "Authenticate with a tenant-bound token or use a verified tenant storefront domain."
            });
            return;
        }

        tenantContext.SetTenantId(effectiveTenant.Id);
        context.Request.Headers[TenantIdHeaderName] = effectiveTenant.Id.ToString();

        _logger.LogDebug("Tenant context set: {TenantId} ({TenantName})", effectiveTenant.Id, effectiveTenant.Name);

        await _next(context);
    }

    private static bool HeaderMatchesTenant(string headerValue, Tenant tenant)
    {
        var candidate = headerValue.Trim();
        if (Guid.TryParse(candidate, out var headerTenantId))
        {
            return headerTenantId != Guid.Empty && headerTenantId == tenant.Id;
        }

        return !string.IsNullOrWhiteSpace(tenant.Slug) &&
               string.Equals(candidate, tenant.Slug, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipTenantResolution(string path)
    {
        return ExcludedPaths.Any(excluded =>
            path.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts the authenticated tenant claim from the current principal.
    /// </summary>
    private static bool TryGetAuthenticatedTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var tenantClaim = user.FindFirstValue("tenant_id") ??
                          user.FindFirstValue("TenantId") ??
                          user.FindFirstValue("tenantId");

        return Guid.TryParse(tenantClaim, out tenantId) && tenantId != Guid.Empty;
    }

    private static async Task<Tenant?> ResolveAuthenticatedTenantAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        string inboundTenantHeader,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedTenantId(context.User, out var tenantId))
        {
            return null;
        }

        // HostResolution already proved the active tenant and generation-fenced
        // that verdict across replicas. A matching claim does not need to repeat
        // the same Tenants query unless a slug-form header needs validation;
        // mismatches still use the guarded lookup below.
        if (context.Items.TryGetValue(HostResolutionContextKeys.DomainResolutionResultItemKey, out var existingResult) &&
            existingResult is DomainResolutionResult domainResolutionResult &&
            domainResolutionResult.Status == DomainResolutionStatus.Found &&
            domainResolutionResult.TenantId == tenantId &&
            (string.IsNullOrWhiteSpace(inboundTenantHeader) ||
             Guid.TryParse(inboundTenantHeader.Trim(), out _)))
        {
            return new Tenant
            {
                Id = tenantId,
                Name = domainResolutionResult.NormalizedHost ?? "resolved-storefront",
                IsActive = true,
                Status = TenantStatus.Active
            };
        }

        return await TenantAuthorityMemo.GetTenantByIdAsync(dbContext, tenantId, cancellationToken);
    }

    private async Task<Tenant?> ResolveRequestTenantAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        Tenant? authenticatedTenant,
        CancellationToken cancellationToken)
    {
        // DOMAIN-2D: Consume the DomainResolutionResult produced by
        // HostResolutionMiddleware (which ran earlier in the pipeline per
        // DOMAIN-1.6 Section 3.9 ordering). If a Domain was resolved
        // (Status == Found with TenantId), use that TenantId directly —
        // no additional host normalization, no additional Domain lookup,
        // no additional repository access. The TenantId was already
        // passed through from the Domain entity by PersistenceDomainResolver.
        if (context.Items.TryGetValue(HostResolutionContextKeys.DomainResolutionResultItemKey, out var existingResult) &&
            existingResult is DomainResolutionResult domainResolutionResult &&
            domainResolutionResult.Status == DomainResolutionStatus.Found &&
            domainResolutionResult.TenantId.HasValue)
        {
            // Host resolution already joined the current active tenant authority and
            // generation-fenced it against Domain/Tenant mutations. Reuse that result;
            // querying Tenants again here recreates the request fan-out this contract
            // exists to remove.
            if (authenticatedTenant?.Id == domainResolutionResult.TenantId.Value)
            {
                return authenticatedTenant;
            }

            return new Tenant
            {
                Id = domainResolutionResult.TenantId.Value,
                Name = domainResolutionResult.NormalizedHost ?? "resolved-storefront",
                IsActive = true,
                Status = TenantStatus.Active
            };
        }

        foreach (var candidateUri in GetTenantCandidateUris(context))
        {
            var tenant = await ResolveTenantFromCandidateUriAsync(candidateUri, dbContext, cancellationToken);
            if (tenant != null)
            {
                return tenant;
            }
        }

        return null;
    }

    private IEnumerable<Uri> GetTenantCandidateUris(HttpContext context)
    {
        // SECURITY: Only trust server-controlled Host header, never client-controlled Origin/Referer
        // An attacker can spoof Origin/Referer to access another tenant's data
        if (TryBuildRequestUri(context.Request.Scheme, context.Request.Host, out var requestUri))
        {
            yield return requestUri;
        }
    }

    private async Task<Tenant?> ResolveTenantFromCandidateUriAsync(
        Uri candidateUri,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // A host matching the configured platform template has a canonical slug.
        // Resolve it directly instead of first issuing a StorefrontBaseUrl query
        // that is expected to miss on every normal storefront request.
        if (TryExtractTenantSlug(candidateUri, out var slug))
        {
            var tenant = await dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    tenant => tenant.Slug == slug &&
                              tenant.IsActive &&
                              tenant.Status == TenantStatus.Active &&
                              tenant.DeletedAtUtc == null,
                    cancellationToken);
            if (tenant != null)
            {
                TenantAuthorityMemo.Store(dbContext, tenant);
            }

            return tenant;
        }

        return await ResolveTenantFromStorefrontBaseUrlAsync(candidateUri, dbContext, cancellationToken);
    }

    private static async Task<Tenant?> ResolveTenantFromStorefrontBaseUrlAsync(
        Uri candidateUri,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalizedBaseUrls = BuildCandidateBaseUrls(candidateUri)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedBaseUrls.Length == 0)
        {
            return null;
        }

        var matches = await dbContext.Tenants
            .AsNoTracking()
            .Where(
                tenant => tenant.Status == TenantStatus.Active &&
                          tenant.IsActive &&
                          tenant.DeletedAtUtc == null &&
                          tenant.StorefrontBaseUrl != null &&
                          normalizedBaseUrls.Contains(tenant.StorefrontBaseUrl.ToLower()))
            .Take(2)
            .ToListAsync(cancellationToken);

        var tenant = matches.Count == 1 ? matches[0] : null;
        if (tenant != null)
        {
            TenantAuthorityMemo.Store(dbContext, tenant);
        }

        return tenant;
    }

    private bool TryExtractTenantSlug(Uri candidateUri, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(_storefrontUrlTemplate) ||
            !_storefrontUrlTemplate.Contains("{slug}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerTemplate = _storefrontUrlTemplate.Replace("{slug}", SlugMarker, StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(markerTemplate, UriKind.Absolute, out var templateUri))
        {
            return false;
        }

        if (!templateUri.IsDefaultPort && candidateUri.Port != templateUri.Port)
        {
            return false;
        }

        var templateHost = templateUri.Host;
        var markerIndex = templateHost.IndexOf(SlugMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var prefix = templateHost[..markerIndex];
        var suffix = templateHost[(markerIndex + SlugMarker.Length)..];
        var candidateHost = candidateUri.Host;

        if (!candidateHost.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !candidateHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var slugLength = candidateHost.Length - prefix.Length - suffix.Length;
        if (slugLength <= 0)
        {
            return false;
        }

        var extracted = candidateHost.Substring(prefix.Length, slugLength);
        if (string.IsNullOrWhiteSpace(extracted) || extracted.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        slug = extracted.ToLowerInvariant();
        return true;
    }

    private static bool TryBuildRequestUri(string scheme, HostString host, out Uri uri)
    {
        uri = null!;
        if (!host.HasValue)
        {
            return false;
        }

        return Uri.TryCreate($"{scheme}://{host.Value}", UriKind.Absolute, out uri);
    }

    private static IEnumerable<string> BuildCandidateBaseUrls(Uri candidateUri)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var authority = candidateUri.IsDefaultPort
            ? candidateUri.Host
            : candidateUri.Authority;

        var normalized = $"{candidateUri.Scheme}://{authority}";
        values.Add(normalized);
        values.Add($"{normalized}/");

        return values;
    }
}
