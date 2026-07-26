using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.Security;

public static class GuestOrderAccessCookie
{
    public const string CheckoutTokenCookieName = "storefront_guest_checkout_token";
    public const string TrackingTokenCookieName = "storefront_guest_tracking_token";

    private const string CookiePath = "/api";

    public static void SetCheckoutToken(HttpContext httpContext, string token, TimeSpan lifetime, bool allowInsecureDevelopmentCookie)
    {
        SetCookie(httpContext, CheckoutTokenCookieName, token, lifetime, allowInsecureDevelopmentCookie);
    }

    public static void SetTrackingToken(HttpContext httpContext, string token, TimeSpan lifetime, bool allowInsecureDevelopmentCookie)
    {
        SetCookie(httpContext, TrackingTokenCookieName, token, lifetime, allowInsecureDevelopmentCookie);
    }

    public static string? ReadCheckoutToken(HttpRequest request)
    {
        return ReadCookie(request, CheckoutTokenCookieName);
    }

    public static string? ReadTrackingToken(HttpRequest request)
    {
        return ReadCookie(request, TrackingTokenCookieName);
    }

    public static void ClearCheckoutToken(HttpContext httpContext, bool allowInsecureDevelopmentCookie)
    {
        ClearCookie(httpContext, CheckoutTokenCookieName, allowInsecureDevelopmentCookie);
    }

    public static void ClearTrackingToken(HttpContext httpContext, bool allowInsecureDevelopmentCookie)
    {
        ClearCookie(httpContext, TrackingTokenCookieName, allowInsecureDevelopmentCookie);
    }

    private static void SetCookie(HttpContext httpContext, string cookieName, string token, TimeSpan lifetime, bool allowInsecureDevelopmentCookie)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        httpContext.Response.Cookies.Append(
            cookieName,
            token.Trim(),
            CreateCookieOptions(lifetime, allowInsecureDevelopmentCookie));
    }

    private static string? ReadCookie(HttpRequest request, string cookieName)
    {
        if (!request.Cookies.TryGetValue(cookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return token.Trim();
    }

    private static void ClearCookie(HttpContext httpContext, string cookieName, bool allowInsecureDevelopmentCookie)
    {
        httpContext.Response.Cookies.Delete(
            cookieName,
            CreateCookieOptions(TimeSpan.FromSeconds(1), allowInsecureDevelopmentCookie));
    }

    private static CookieOptions CreateCookieOptions(TimeSpan lifetime, bool allowInsecureDevelopmentCookie) => new()
    {
        HttpOnly = true,
        Secure = !allowInsecureDevelopmentCookie,
        SameSite = SameSiteMode.Strict,
        MaxAge = lifetime,
        Path = CookiePath,
        IsEssential = true
    };
}
