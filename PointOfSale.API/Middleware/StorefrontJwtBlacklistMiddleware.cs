using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Data;
using System.Security.Claims;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Middleware to enforce Storefront access-token revocation.
/// 
/// SECURITY CRITICAL:
/// - Checks revocation for every authenticated Storefront request by JWT ID (jti)
/// - Uses Redis as a cache and PostgreSQL as the durable source of truth
/// - Fails closed when revocation status cannot be verified
/// 
/// BLACKLIST TRIGGERS:
/// - Logout
/// - Account deactivation
/// - Token reuse detection
/// - All tokens revoked
/// 
/// SECURITY BOUNDARY:
/// This middleware ONLY checks Storefront tokens (identified by issuer).
/// POS tokens are NOT checked here.
/// 
/// Phase: 10.3.x - Authentication Hardening
/// </summary>
public class StorefrontJwtBlacklistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<StorefrontJwtBlacklistMiddleware> _logger;

    // Resolved from StorefrontJwtSettings so it stays in sync with token generation.
    private readonly string _storefrontIssuer;

    public StorefrontJwtBlacklistMiddleware(
        RequestDelegate next,
        ILogger<StorefrontJwtBlacklistMiddleware> logger,
        IOptions<StorefrontJwtSettings> jwtSettings)
    {
        _next = next;
        _logger = logger;
        _storefrontIssuer = jwtSettings.Value.Issuer;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IStorefrontJwtBlacklist blacklist,
        ApplicationDbContext dbContext)
    {
        // SECURITY CRITICAL: Explicitly authenticate the StorefrontJwt scheme.
        // At this point in the pipeline, context.User only contains the default scheme's principal.
        // Endpoints with [Authorize(AuthenticationSchemes = "StorefrontJwt")] won't have their
        // principal set until authorization runs later, so we must authenticate explicitly here
        // to catch revoked tokens before they reach the endpoint.
        var authenticateResult = await context.AuthenticateAsync("StorefrontJwt");

        // If authentication failed or no result, skip blacklist check
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            await _next(context);
            return;
        }

        // Check if this is a Storefront JWT (by issuer claim).
        // Compare against the configured issuer so any environment-specific
        // override in appsettings is honoured, not a hard-coded literal.
        var issuer = authenticateResult.Principal.FindFirst("iss")?.Value;
        if (issuer != _storefrontIssuer)
        {
            // Not a Storefront token - skip blacklist check
            await _next(context);
            return;
        }

        // Extract required claims from the authenticated principal
        var jti = authenticateResult.Principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var tenantIdClaim = authenticateResult.Principal.FindFirst("TenantId")?.Value;
        var customerIdClaim = authenticateResult.Principal.FindFirst("CustomerId")?.Value;

        if (string.IsNullOrEmpty(jti) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId) ||
            !Guid.TryParse(customerIdClaim, out var customerId))
        {
            _logger.LogWarning(
                "[JwtBlacklistMiddleware] INVALID_STOREFRONT_TOKEN_CLAIMS | Path: {Path}",
                PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

            await RejectTokenAsync(
                context,
                "INVALID_TOKEN",
                "The authentication token is missing required claims.");
            return;
        }

        try
        {
            if (await blacklist.IsBlacklistedAsync(tenantId, jti, context.RequestAborted))
            {
                _logger.LogWarning(
                    "[JwtBlacklistMiddleware] JWT_BLACKLIST_HIT | TenantId: {TenantId} | Jti: {Jti} | Path: {Path}",
                    tenantId,
                    jti,
                    PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

                await RejectTokenAsync(
                    context,
                    "TOKEN_REVOKED",
                    "Your session has been revoked. Please log in again.");
                return;
            }

            var customerIsActive = await dbContext.OnlineCustomers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(customer => customer.Id == customerId && customer.TenantId == tenantId)
                .Select(customer => new
                {
                    customer.IsActive,
                    customer.TokensValidAfterUtc
                })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (customerIsActive == null || !customerIsActive.IsActive)
            {
                _logger.LogWarning(
                    "[JwtBlacklistMiddleware] STOREFRONT_CUSTOMER_DEACTIVATED | TenantId: {TenantId} | CustomerId: {CustomerId} | Path: {Path}",
                    tenantId,
                    customerId,
                    PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

                await RejectTokenAsync(
                    context,
                    "ACCOUNT_DEACTIVATED",
                    "This account is no longer active.");
                return;
            }

            // SECURITY: Check durable cutoff timestamp
            // If TokensValidAfterUtc is set, reject any token issued before that timestamp
            if (customerIsActive.TokensValidAfterUtc.HasValue)
            {
                var tokenIssuedAt = ResolveTokenIssuedAtUtc(authenticateResult.Principal);
                if (tokenIssuedAt.HasValue)
                {
                    if (tokenIssuedAt.Value <= customerIsActive.TokensValidAfterUtc.Value)
                    {
                        _logger.LogWarning(
                            "[JwtBlacklistMiddleware] TOKEN_ISSUED_BEFORE_CUTOFF | TenantId: {TenantId} | CustomerId: {CustomerId} | TokenIssuedAt: {TokenIssuedAt} | CutoffTime: {CutoffTime} | Path: {Path}",
                            tenantId,
                            customerId,
                            tokenIssuedAt.Value,
                            customerIsActive.TokensValidAfterUtc.Value,
                            PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

                        await RejectTokenAsync(
                            context,
                            "TOKEN_REVOKED",
                            "Your session has been revoked. Please log in again.");
                        return;
                    }
                }
                else
                {
                    // SECURITY: Reject tokens with missing/unparseable iat when cutoff is enforced
                    // Defense-in-depth: if we can't evaluate the cutoff, reject the token
                    _logger.LogWarning(
                        "[JwtBlacklistMiddleware] TOKEN_MISSING_IAT_WITH_CUTOFF | TenantId: {TenantId} | CustomerId: {CustomerId} | Path: {Path} | IatClaim: {IatClaim}",
                        tenantId,
                        customerId,
                        PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"),
                        authenticateResult.Principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value ?? "(null)");

                    await RejectTokenAsync(
                        context,
                        "TOKEN_REVOKED",
                        "Your session has been revoked. Please log in again.");
                    return;
                }
            }

            await _next(context);
        }
        catch (TokenRevocationCheckUnavailableException ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklistMiddleware] Revocation check unavailable, rejecting request | TenantId: {TenantId} | CustomerId: {CustomerId} | Path: {Path}",
                tenantId,
                customerId,
                PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

            await RejectTokenAsync(
                context,
                "TOKEN_VALIDATION_UNAVAILABLE",
                "Unable to verify the current session. Please authenticate again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklistMiddleware] Storefront token validation failed closed | TenantId: {TenantId} | CustomerId: {CustomerId} | Path: {Path}",
                tenantId,
                customerId,
                PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));

            await RejectTokenAsync(
                context,
                "TOKEN_VALIDATION_UNAVAILABLE",
                "Unable to verify the current session. Please authenticate again.");
        }
    }

    private static async Task RejectTokenAsync(HttpContext context, string error, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error,
            message
        });
    }

    private static DateTime? ResolveTokenIssuedAtUtc(ClaimsPrincipal principal)
    {
        var preciseIssuedAtClaim = principal.FindFirst(StorefrontSecurityConstants.SessionIssuedAtTicksClaim)?.Value;
        if (long.TryParse(preciseIssuedAtClaim, out var preciseIssuedAtTicks))
        {
            try
            {
                return new DateTime(preciseIssuedAtTicks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        var iatClaim = principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
        if (long.TryParse(iatClaim, out var iatUnixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(iatUnixSeconds).UtcDateTime;
        }

        return null;
    }
}

/// <summary>
/// Extension methods for registering JWT blacklist middleware.
/// </summary>
public static class StorefrontJwtBlacklistMiddlewareExtensions
{
    /// <summary>
    /// Adds Storefront JWT blacklist checking middleware.
    /// Must be called AFTER UseAuthentication() and BEFORE UseAuthorization().
    /// </summary>
    public static IApplicationBuilder UseStorefrontJwtBlacklist(this IApplicationBuilder app)
    {
        return app.UseMiddleware<StorefrontJwtBlacklistMiddleware>();
    }
}
