using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Host Resolution Middleware (DOMAIN-2A Foundation + DOMAIN-2C Domain Resolution).
///
/// Responsibilities:
///   - Read <c>Request.Host</c> (now corrected by ForwardedHeaders with
///     <c>X-Forwarded-Host</c> support).
///   - Normalize the host via <see cref="IHostNormalizer"/>.
///   - Store the normalized host in <see cref="IHostResolutionContext"/>.
///   - Resolve the normalized host to a <see cref="DomainResolutionResult"/>
///     via <see cref="IDomainResolver"/> (DOMAIN-2C).
///   - Store the resolution result in <see cref="HttpContext.Items"/> under
///     <see cref="HostResolutionContextKeys.DomainResolutionResultItemKey"/>.
///   - Log structured diagnostics (NormalizedHost, ResolutionStatus,
///     CacheAttempted, CorrelationId).
///   - Begin redirect handling for redirect domains (DOMAIN-1.6 Section 4).
///   - Continue the pipeline.
///
/// In DOMAIN-2C, the <see cref="IDomainResolver"/> is persistence-backed —
/// it queries the Domains table by NormalizedHostName and returns
/// <see cref="DomainResolutionStatus.Found"/> or <see cref="DomainResolutionStatus.NotFound"/>.
/// The middleware performs no direct EF Core, tenant, StoreConfig, Theme, CMS,
/// cache, or preview lookup. The DOMAIN-2C resolver owns the Domain registry
/// lookup and passes through Domain.TenantId without resolving the Tenant entity.
///
/// Pipeline ordering (DOMAIN-1.6 Section 3.9, DOMAIN-1 Section 7.2):
///   - AFTER UseForwardedHeaders (for correct Request.Host)
///   - AFTER Authentication (for JWT cross-check)
///   - AFTER ThemePreviewAuthorityMiddleware (preview tokens bypass host resolution)
///   - BEFORE TenantResolutionMiddleware (provides NormalizedHost for downstream)
///
/// Resolution pipeline (DOMAIN-2C):
///   Host → Normalize (DOMAIN-2A) → IDomainResolver → DomainResolutionResult → STOP
///   No Tenant.
///
/// Excluded paths match TenantResolutionMiddleware exclusions so that
/// platform, health, and test-harness endpoints are not processed.
/// </summary>
public sealed class HostResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HostResolutionMiddleware> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// Header name set by ForwardedHeaders middleware when <c>X-Forwarded-Host</c>
    /// is present on the inbound request. This middleware logs whether the
    /// forwarded host was used (i.e., the host differs from the internal
    /// proxy host).
    /// </summary>
    internal const string ForwardedHostHeader = "X-Forwarded-Host";

    /// <summary>
    /// Paths that bypass host resolution — same exclusion set as
    /// TenantResolutionMiddleware to maintain pipeline consistency.
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
        "/api/affiliate",
        "/api/test-auth"
    };

    public HostResolutionMiddleware(
        RequestDelegate next,
        ILogger<HostResolutionMiddleware> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _next = next;
        _logger = logger;
        _correlationIdAccessor = correlationIdAccessor;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ShouldSkipHostResolution(path))
        {
            await _next(context);
            return;
        }

        var rawHost = GetRawHost(context);
        var hostNormalizer = context.RequestServices.GetRequiredService<IHostNormalizer>();
        var domainResolver = context.RequestServices.GetRequiredService<IDomainResolver>();
        var normalizedHost = hostNormalizer.Normalize(rawHost);
        var forwardedHostUsed = IsForwardedHostUsed(context);
        var correlationId = _correlationIdAccessor.GetCorrelationId();

        if (normalizedHost != null)
        {
            // Store the normalized host in the per-request runtime context so
            // that downstream middleware (TenantResolutionMiddleware, future
            // Domain resolution) can read a trusted, canonical host value.
            var hostResolutionContext = context.RequestServices.GetService<IHostResolutionContext>();
            if (hostResolutionContext != null)
            {
                hostResolutionContext.SetNormalizedHost(normalizedHost);
            }

            // Store in HttpContext.Items as well for middleware that runs
            // outside the DI scope chain (e.g., inline Use() delegates).
            context.Items[HostResolutionContextKeys.NormalizedHostItemKey] = normalizedHost;

            // DOMAIN-2C: Resolve the normalized host to a Domain Resolution Result.
            // Uses the persistence-backed resolver — queries the Domains table
            // by NormalizedHostName via IDomainRepository.
            var resolutionResult = await domainResolver.ResolveAsync(normalizedHost, context.RequestAborted);
            context.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] = resolutionResult;

            // Redirect handling (DOMAIN-1.6 Section 4, Constitutional Runtime):
            // If the resolved Domain has a redirect configured, issue the redirect
            // before continuing the pipeline. This happens BEFORE StoreConfig/Theme/CMS
            // resolution — the redirect is a host-level concern.
            if (resolutionResult.Status == DomainResolutionStatus.Found &&
                resolutionResult.RedirectType.HasValue &&
                resolutionResult.RedirectType != RedirectType.None &&
                !string.IsNullOrEmpty(resolutionResult.RedirectTargetNormalizedHost))
            {
                var redirectTarget = resolutionResult.RedirectTargetNormalizedHost;
                var redirectType = resolutionResult.RedirectType.Value;

                // Preserve the original scheme and path/query
                var originalPath = context.Request.Path.Value ?? string.Empty;
                var originalQuery = context.Request.QueryString.Value ?? string.Empty;
                var scheme = context.Request.Scheme;

                var statusCode = redirectType switch
                {
                    RedirectType.Permanent301 => 301,
                    RedirectType.Temporary307 => 307,
                    _ => 301
                };

                var targetUrl = $"{scheme}://{redirectTarget}{originalPath}{originalQuery}";

                using (_logger.BeginScope(new Dictionary<string, object?>
                {
                    [nameof(CorrelationId)] = correlationId,
                    [nameof(Host)] = rawHost ?? string.Empty,
                    [nameof(NormalizedHost)] = normalizedHost ?? string.Empty,
                    [nameof(ResolutionStatus)] = resolutionResult.Status,
                    [nameof(CacheAttempted)] = resolutionResult.CacheAttempted,
                    [nameof(ForwardedHostUsed)] = forwardedHostUsed,
                    [RedirectTypeLogProp] = redirectType,
                    [RedirectTargetLogProp] = redirectTarget
                }))
                {
                    _logger.LogDebug(
                        "Host resolved with redirect. Host={Host} NormalizedHost={NormalizedHost} RedirectType={RedirectType} RedirectTarget={RedirectTarget} ToUrl={TargetUrl} CorrelationId={CorrelationId}",
                        rawHost ?? string.Empty,
                        normalizedHost ?? string.Empty,
                        redirectType,
                        redirectTarget,
                        targetUrl,
                        correlationId);
                }

                context.Response.Redirect(targetUrl, redirectType == RedirectType.Permanent301);
                return;
            }

            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                [nameof(CorrelationId)] = correlationId,
                [nameof(Host)] = rawHost ?? string.Empty,
                [nameof(NormalizedHost)] = normalizedHost ?? string.Empty,
                [nameof(ResolutionStatus)] = resolutionResult.Status,
                [nameof(CacheAttempted)] = resolutionResult.CacheAttempted,
                [nameof(ForwardedHostUsed)] = forwardedHostUsed
            }))
            {
                _logger.LogDebug(
                    "Host resolved. Host={Host} NormalizedHost={NormalizedHost} ResolutionStatus={ResolutionStatus} CacheAttempted={CacheAttempted} ForwardedHostUsed={ForwardedHostUsed} CorrelationId={CorrelationId}",
                    rawHost ?? string.Empty,
                    normalizedHost ?? string.Empty,
                    resolutionResult.Status,
                    resolutionResult.CacheAttempted,
                    forwardedHostUsed,
                    correlationId);
            }

            await _next(context);
            return;
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            [nameof(CorrelationId)] = correlationId,
            [nameof(Host)] = rawHost ?? string.Empty,
            [nameof(NormalizedHost)] = string.Empty,
            [nameof(ResolutionStatus)] = DomainResolutionStatus.NotFound,
            [nameof(CacheAttempted)] = false,
            [nameof(ForwardedHostUsed)] = forwardedHostUsed
        }))
        {
            _logger.LogDebug(
                "Host resolved. Host={Host} NormalizedHost={NormalizedHost} ResolutionStatus={ResolutionStatus} CacheAttempted={CacheAttempted} ForwardedHostUsed={ForwardedHostUsed} CorrelationId={CorrelationId}",
                rawHost ?? string.Empty,
                string.Empty,
                DomainResolutionStatus.NotFound,
                false,
                forwardedHostUsed,
                correlationId);
        }

        await _next(context);
    }

    private static bool ShouldSkipHostResolution(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        // Exact match on root or excluded path prefix.
        foreach (var excluded in ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the raw host string from the request. After <see cref="UseForwardedHeaders"/>
    /// processes <c>X-Forwarded-Host</c>, <c>context.Request.Host</c> contains
    /// the true customer host. This method reads that value.
    /// </summary>
    private static string? GetRawHost(HttpContext context)
    {
        return context.Request.Host.Value;
    }

    /// <summary>
    /// Determines whether the effective host was derived from an inbound
    /// <c>X-Forwarded-Host</c> header (as opposed to the proxy-set Host header
    /// directly). This is a diagnostic signal for structured logging.
    /// </summary>
    private static bool IsForwardedHostUsed(HttpContext context)
    {
        // The ForwardedHeaders middleware populates Request.Host from
        // X-Forwarded-Host. We detect this by checking if the original
        // X-Forwarded-Host header was present on the inbound request.
        return context.Request.Headers.ContainsKey(ForwardedHostHeader);
    }

    // Structured log property names — kept as constants so test assertions
    // and log-consumption tooling reference the same identifiers.
    private const string Host = nameof(Host);
    private const string NormalizedHost = nameof(NormalizedHost);
    private const string ResolutionStatus = nameof(ResolutionStatus);
    private const string CacheAttempted = nameof(CacheAttempted);
    private const string ForwardedHostUsed = nameof(ForwardedHostUsed);
    private const string CorrelationId = nameof(CorrelationId);
    private const string RedirectTypeLogProp = "RedirectType";
    private const string RedirectTargetLogProp = "RedirectTarget";
}
