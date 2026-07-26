using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.Security;

public static class StorefrontRateLimitSession
{
    public const string CookieName = "storefront_rl_sid";

    public static async Task<string> ResolveTrustedSubjectAsync(HttpContext httpContext, string clientIp)
    {
        // Explicitly authenticate against storefront schemes to avoid collapsing authenticated customers into IP bucket
        // Priority: 1) StorefrontJwt, 2) CustomerJwt, 3) Session cookie, 4) Anonymous partition
        
        // Try StorefrontJwt scheme first
        var storefrontAuth = await httpContext.AuthenticateAsync("StorefrontJwt");
        if (storefrontAuth?.Succeeded == true && storefrontAuth.Principal != null)
        {
            var customerId = ExtractCustomerId(storefrontAuth.Principal);
            if (customerId.HasValue)
            {
                return $"customer:{customerId.Value:N}";
            }
        }

        // Try CustomerJwt scheme
        var customerAuth = await httpContext.AuthenticateAsync("CustomerJwt");
        if (customerAuth?.Succeeded == true && customerAuth.Principal != null)
        {
            var customerId = ExtractCustomerId(customerAuth.Principal);
            if (customerId.HasValue)
            {
                return $"customer:{customerId.Value:N}";
            }
        }

        // Fall back to session cookie for anonymous users
        if (TryGetServerIssuedSessionId(httpContext.Request, out var sessionId))
        {
            return $"session:{sessionId}";
        }

        // Last resort: stable anonymous partition using IP-salted hash
        // This prevents raw IP exposure and provides better guest isolation
        return $"anonymous:{ComputeStableAnonymousId(clientIp)}";
    }

    public static string ResolveTrustedSubject(HttpContext httpContext, string clientIp)
    {
        // Synchronous wrapper for backward compatibility
        // Note: This will use the default HttpContext.User which may be the POS bearer
        // For proper storefront rate limiting, use ResolveTrustedSubjectAsync
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirst("CustomerId")?.Value
                         ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? user.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(userId) && Guid.TryParse(userId, out var userGuid))
            {
                return $"customer:{userGuid:N}";
            }
        }

        if (TryGetServerIssuedSessionId(httpContext.Request, out var sessionId))
        {
            return $"session:{sessionId}";
        }

        return $"anonymous:{ComputeStableAnonymousId(clientIp)}";
    }

    private static Guid? ExtractCustomerId(ClaimsPrincipal principal)
    {
        var customerIdClaim = principal.FindFirst("CustomerId")?.Value
                              ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? principal.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(customerIdClaim) && Guid.TryParse(customerIdClaim, out var customerId))
        {
            return customerId;
        }

        return null;
    }

    private static string ComputeStableAnonymousId(string clientIp)
    {
        // Create a stable hash of the IP address for anonymous partition
        // This provides better isolation than raw IP while maintaining stability per IP
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"anonymous-rl-{clientIp}"));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static void EnsureServerIssuedSessionCookie(HttpContext httpContext, bool allowInsecureDevelopmentCookie)
    {
        if (!ShouldIssueCookie(httpContext.Request))
        {
            return;
        }

        if (TryGetServerIssuedSessionId(httpContext.Request, out _))
        {
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Cookies.Append(
                CookieName,
                sessionId,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !allowInsecureDevelopmentCookie,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(30),
                    IsEssential = true
                });

            return Task.CompletedTask;
        });
    }

    public static bool TryGetServerIssuedSessionId(HttpRequest request, out string sessionId)
    {
        sessionId = string.Empty;

        if (!request.Cookies.TryGetValue(CookieName, out var cookieValue) ||
            string.IsNullOrWhiteSpace(cookieValue))
        {
            return false;
        }

        var normalized = cookieValue.Trim();
        if (normalized.Length != 32 ||
            !normalized.All(static c => char.IsAsciiHexDigit(c)))
        {
            return false;
        }

        sessionId = normalized.ToLowerInvariant();
        return true;
    }

    private static bool ShouldIssueCookie(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/storefront", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/payments", StringComparison.OrdinalIgnoreCase);
    }
}
