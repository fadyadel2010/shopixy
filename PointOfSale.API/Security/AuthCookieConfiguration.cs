using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PointOfSale.API.Security;

public sealed record AuthCookieScope(string AccessTokenCookieName, string RefreshTokenCookieName);

public static class AuthCookieConfiguration
{
    public const string PosAccessTokenCookieName = "access_token";
    public const string PosRefreshTokenCookieName = "refresh_token";
    public const string LegacyPosAccessTokenCookieName = "auth_token";

    public const string AffiliateAccessTokenCookieName = "affiliate_access_token";
    public const string AffiliateRefreshTokenCookieName = "affiliate_refresh_token";

    public const string SuperAdminAccessTokenCookieName = "super_admin_access_token";
    public const string SuperAdminRefreshTokenCookieName = "super_admin_refresh_token";
    public const string StorefrontAccessTokenCookieName = "storefront_access_token";
    public const string StorefrontRefreshTokenCookieName = "storefront_refresh_token";
    public const string CommerceHubCustomerAccessTokenCookieName = "commercehub_customer_access_token";

    public const string CsrfCookieName = "csrf_token";
    public const string CsrfHeaderName = "X-CSRF-Token";

    public static readonly AuthCookieScope Pos = new(PosAccessTokenCookieName, PosRefreshTokenCookieName);
    public static readonly AuthCookieScope Affiliate = new(AffiliateAccessTokenCookieName, AffiliateRefreshTokenCookieName);
    public static readonly AuthCookieScope SuperAdmin = new(SuperAdminAccessTokenCookieName, SuperAdminRefreshTokenCookieName);

    private static readonly string[] AllAccessCookieNames =
    [
        PosAccessTokenCookieName,
        LegacyPosAccessTokenCookieName,
        AffiliateAccessTokenCookieName,
        SuperAdminAccessTokenCookieName,
        StorefrontAccessTokenCookieName,
        CommerceHubCustomerAccessTokenCookieName
    ];

    private static readonly string[] AllRefreshCookieNames =
    [
        PosRefreshTokenCookieName,
        AffiliateRefreshTokenCookieName,
        SuperAdminRefreshTokenCookieName,
        StorefrontRefreshTokenCookieName
    ];

    public static void SetSessionCookies(
        HttpContext httpContext,
        AuthCookieScope scope,
        string accessToken,
        TimeSpan accessTokenLifetime,
        string refreshToken,
        TimeSpan refreshTokenLifetime)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        var useSecureCookies = ShouldUseSecureCookies(httpContext);

        response.Cookies.Append(
            scope.AccessTokenCookieName,
            accessToken,
            CreateRequestHttpOnlyOptions(accessTokenLifetime, useSecureCookies));

        response.Cookies.Append(
            scope.RefreshTokenCookieName,
            refreshToken,
            CreateRequestHttpOnlyOptions(refreshTokenLifetime, useSecureCookies));

        if (scope == Pos)
        {
            response.Cookies.Delete(LegacyPosAccessTokenCookieName, CreateRequestExpiredOptions(useSecureCookies));
        }

        EnsureCsrfCookie(httpContext);
    }

    public static void ClearSessionCookies(HttpContext httpContext, AuthCookieScope scope)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        var expiredOptions = CreateRequestExpiredOptions(ShouldUseSecureCookies(httpContext));

        response.Cookies.Delete(scope.AccessTokenCookieName, expiredOptions);
        response.Cookies.Delete(scope.RefreshTokenCookieName, expiredOptions);

        if (scope == Pos)
        {
            response.Cookies.Delete(LegacyPosAccessTokenCookieName, expiredOptions);
        }
    }

    public static string EnsureCsrfCookie(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Request.Cookies.TryGetValue(CsrfCookieName, out var existingToken) &&
            !string.IsNullOrWhiteSpace(existingToken))
        {
            return existingToken;
        }

        var csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        httpContext.Response.Cookies.Append(
            CsrfCookieName,
            csrfToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = ShouldUseSecureCookies(httpContext),
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromDays(30),
                Path = "/",
                IsEssential = true
            });

        return csrfToken;
    }

    public static bool HasAnyAuthenticationCookie(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AllAccessCookieNames.Concat(AllRefreshCookieNames)
            .Any(cookieName => request.Cookies.ContainsKey(cookieName));
    }

    public static bool TryReadJwtExpiryUtc(string? token, out DateTime expiresAtUtc)
    {
        expiresAtUtc = default;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        var jwtToken = handler.ReadJwtToken(token);
        if (jwtToken.ValidTo == DateTime.MinValue)
        {
            return false;
        }

        expiresAtUtc = DateTime.SpecifyKind(jwtToken.ValidTo, DateTimeKind.Utc);
        return true;
    }

    public static bool IsValidCsrfToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Cookies.TryGetValue(CsrfCookieName, out var csrfCookie) ||
            string.IsNullOrWhiteSpace(csrfCookie))
        {
            return false;
        }

        if (!request.Headers.TryGetValue(CsrfHeaderName, out var csrfHeaderValues))
        {
            return false;
        }

        var csrfHeader = csrfHeaderValues.ToString();
        if (string.IsNullOrWhiteSpace(csrfHeader))
        {
            return false;
        }

        var cookieBytes = System.Text.Encoding.UTF8.GetBytes(csrfCookie);
        var headerBytes = System.Text.Encoding.UTF8.GetBytes(csrfHeader);

        return CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes);
    }

    public static CookieOptions CreateHttpOnlyOptions(TimeSpan lifetime, string path = "/") => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        MaxAge = lifetime,
        Path = path,
        IsEssential = true
    };

    public static CookieOptions CreateExpiredOptions(string path = "/") => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UnixEpoch,
        Path = path,
        IsEssential = true
    };

    private static CookieOptions CreateRequestHttpOnlyOptions(TimeSpan lifetime, bool secure)
    {
        var options = CreateHttpOnlyOptions(lifetime);
        options.Secure = secure;
        return options;
    }

    private static CookieOptions CreateRequestExpiredOptions(bool secure)
    {
        var options = CreateExpiredOptions();
        options.Secure = secure;
        return options;
    }

    private static bool ShouldUseSecureCookies(HttpContext httpContext)
    {
        var services = httpContext.Features.Get<IServiceProvidersFeature>()?.RequestServices;
        var environment = services?.GetService<IHostEnvironment>();

        return httpContext.Request.IsHttps || environment?.IsDevelopment() != true;
    }
}
