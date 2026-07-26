using PointOfSale.API.Security;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Middleware that resolves the trusted rate limit subject once per request and caches it in HttpContext.Items.
/// This prevents sync-over-async blocking in rate limit partition factories which must be synchronous.
/// Must run AFTER UseAuthentication() and BEFORE UseRateLimiter().
/// </summary>
public sealed class RateLimitSubjectResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public const string CacheKey = "RateLimitSubject";

    public RateLimitSubjectResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // UseForwardedHeaders middleware normalizes RemoteIpAddress based on X-Forwarded-For
        // when the request comes from a known proxy, so we can trust RemoteIpAddress directly.
        // Do NOT manually parse X-Forwarded-For as it bypasses trusted proxy validation.
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Resolve trusted subject asynchronously and cache in HttpContext.Items
        var trustedSubject = await StorefrontRateLimitSession.ResolveTrustedSubjectAsync(context, clientIp);
        context.Items[CacheKey] = trustedSubject;

        await _next(context);
    }
}
