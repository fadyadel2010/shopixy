using PointOfSale.API.Security;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Configuration for Storefront refresh token cookies.
/// 
/// SECURITY RATIONALE FOR COOKIES:
/// HttpOnly cookies prevent XSS attacks from stealing refresh tokens.
/// Unlike localStorage/sessionStorage, HttpOnly cookies are:
/// - NOT accessible via JavaScript (document.cookie won't show them)
/// - Automatically sent with requests (no manual handling)
/// - Protected from cross-site request forgery with SameSite attribute
/// 
/// SECURITY SETTINGS:
/// - HttpOnly = true: Prevents XSS token theft
/// - Secure = true: HTTPS only (except in Development)
/// - SameSite = Strict: Prevents CSRF attacks
/// - Path = /api/storefront/auth: Limits cookie scope
/// 
/// Phase: 10.3.x - Authentication Hardening
/// </summary>
public class StorefrontCookieSettings
{
    public const string SectionName = "StorefrontCookie";

    /// <summary>
    /// Cookie name for access token.
    /// </summary>
    public string AccessTokenCookieName { get; set; } = AuthCookieConfiguration.StorefrontAccessTokenCookieName;

    /// <summary>
    /// Cookie path for access token.
    /// </summary>
    public string AccessTokenCookiePath { get; set; } = "/api/storefront";

    /// <summary>
    /// Cookie name for refresh token.
    /// </summary>
    public string RefreshTokenCookieName { get; set; } = AuthCookieConfiguration.StorefrontRefreshTokenCookieName;

    /// <summary>
    /// Cookie name for tenant ID (needed for cookie-based refresh).
    /// </summary>
    public string TenantIdCookieName { get; set; } = "storefront_tenant_id";

    /// <summary>
    /// Cookie path - restricts scope to auth endpoints.
    /// </summary>
    public string CookiePath { get; set; } = "/api/storefront/auth";

    /// <summary>
    /// SameSite mode - Strict prevents CSRF, Lax allows some cross-site.
    /// </summary>
    public string SameSiteMode { get; set; } = "Strict";

    /// <summary>
    /// Whether to allow non-HTTPS cookies (development only).
    /// </summary>
    public bool AllowNonSecureInDevelopment { get; set; } = true;

    /// <summary>
    /// Cookie domain (leave empty for current domain).
    /// </summary>
    public string? Domain { get; set; }
}

/// <summary>
/// Helper for setting and clearing refresh token cookies.
/// 
/// SECURITY BOUNDARY COMMENT:
/// This helper is ONLY for Storefront refresh tokens.
/// POS authentication does NOT use cookies for refresh tokens.
/// </summary>
public interface IStorefrontCookieHelper
{
    /// <summary>
    /// Sets the access token cookie with secure settings.
    /// </summary>
    void SetAccessTokenCookie(
        Microsoft.AspNetCore.Http.HttpResponse response,
        string accessToken,
        DateTime expiresAtUtc);

    /// <summary>
    /// Clears the access token cookie.
    /// </summary>
    void ClearAccessTokenCookie(Microsoft.AspNetCore.Http.HttpResponse response);

    /// <summary>
    /// Sets the refresh token cookie with secure settings.
    /// </summary>
    void SetRefreshTokenCookie(
        Microsoft.AspNetCore.Http.HttpResponse response,
        string refreshToken,
        Guid tenantId,
        DateTime expiresAtUtc);

    /// <summary>
    /// Clears the refresh token cookie.
    /// </summary>
    void ClearRefreshTokenCookie(Microsoft.AspNetCore.Http.HttpResponse response);

    /// <summary>
    /// Gets the refresh token from cookie.
    /// </summary>
    string? GetRefreshTokenFromCookie(Microsoft.AspNetCore.Http.HttpRequest request);

    /// <summary>
    /// Gets the tenant ID from cookie.
    /// </summary>
    Guid? GetTenantIdFromCookie(Microsoft.AspNetCore.Http.HttpRequest request);

    /// <summary>
    /// Gets the access token from cookie.
    /// </summary>
    string? GetAccessTokenFromCookie(Microsoft.AspNetCore.Http.HttpRequest request);
}

public class StorefrontCookieHelper : IStorefrontCookieHelper
{
    private readonly StorefrontCookieSettings _settings;
    private readonly IWebHostEnvironment _environment;

    public StorefrontCookieHelper(
        Microsoft.Extensions.Options.IOptions<StorefrontCookieSettings> settings,
        IWebHostEnvironment environment)
    {
        _settings = settings.Value;
        _environment = environment;
    }

    public void SetRefreshTokenCookie(
        Microsoft.AspNetCore.Http.HttpResponse response,
        string refreshToken,
        Guid tenantId,
        DateTime expiresAtUtc)
    {
        var refreshCookieOptions = CreateRefreshCookieOptions(expiresAtUtc);

        // Set refresh token cookie
        response.Cookies.Append(
            _settings.RefreshTokenCookieName,
            refreshToken,
            refreshCookieOptions);

        // Set tenant ID cookie (needed for cookie-based refresh without header)
        response.Cookies.Append(
            _settings.TenantIdCookieName,
            tenantId.ToString(),
            refreshCookieOptions);
    }

    public void ClearRefreshTokenCookie(Microsoft.AspNetCore.Http.HttpResponse response)
    {
        var expiredOptions = CreateRefreshCookieOptions(DateTime.UtcNow.AddDays(-1));

        response.Cookies.Delete(_settings.RefreshTokenCookieName, expiredOptions);
        response.Cookies.Delete(_settings.TenantIdCookieName, expiredOptions);
    }

    public void SetAccessTokenCookie(
        Microsoft.AspNetCore.Http.HttpResponse response,
        string accessToken,
        DateTime expiresAtUtc)
    {
        response.Cookies.Append(
            _settings.AccessTokenCookieName,
            accessToken,
            CreateAccessCookieOptions(expiresAtUtc));
    }

    public void ClearAccessTokenCookie(Microsoft.AspNetCore.Http.HttpResponse response)
    {
        response.Cookies.Delete(
            _settings.AccessTokenCookieName,
            CreateAccessCookieOptions(DateTime.UtcNow.AddDays(-1)));
    }

    public string? GetRefreshTokenFromCookie(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        if (request.Cookies.TryGetValue(_settings.RefreshTokenCookieName, out var token))
        {
            return token;
        }
        return null;
    }

    public Guid? GetTenantIdFromCookie(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        if (request.Cookies.TryGetValue(_settings.TenantIdCookieName, out var tenantId) &&
            Guid.TryParse(tenantId, out var guid) && guid != Guid.Empty)
        {
            return guid;
        }
        return null;
    }

    public string? GetAccessTokenFromCookie(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        return request.Cookies.TryGetValue(_settings.AccessTokenCookieName, out var token)
            ? token
            : null;
    }

    private Microsoft.AspNetCore.Http.CookieOptions CreateRefreshCookieOptions(DateTime expiresAtUtc)
    {
        return CreateCookieOptions(expiresAtUtc, _settings.CookiePath);
    }

    private Microsoft.AspNetCore.Http.CookieOptions CreateAccessCookieOptions(DateTime expiresAtUtc)
    {
        return CreateCookieOptions(expiresAtUtc, _settings.AccessTokenCookiePath);
    }

    private Microsoft.AspNetCore.Http.CookieOptions CreateCookieOptions(DateTime expiresAtUtc, string path)
    {
        var sameSite = _settings.SameSiteMode.ToLowerInvariant() switch
        {
            "lax" => Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            "none" => Microsoft.AspNetCore.Http.SameSiteMode.None,
            _ => Microsoft.AspNetCore.Http.SameSiteMode.Strict
        };

        // SECURITY: Determine if we need Secure flag
        // In development, allow non-HTTPS if configured
        var isSecure = !(_environment.IsDevelopment() && _settings.AllowNonSecureInDevelopment);

        return new Microsoft.AspNetCore.Http.CookieOptions
        {
            // SECURITY CRITICAL: HttpOnly prevents JavaScript access (XSS protection)
            HttpOnly = true,

            // SECURITY: Only send over HTTPS (except dev)
            Secure = isSecure,

            // SECURITY: Prevents CSRF attacks
            SameSite = sameSite,

            // Scope cookie to the relevant Storefront API path
            Path = path,

            // Set expiry
            Expires = expiresAtUtc,

            // Domain if specified
            Domain = _settings.Domain
        };
    }
}
