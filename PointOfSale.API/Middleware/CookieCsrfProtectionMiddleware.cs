using System.Text.Json;
using PointOfSale.API.Security;

namespace PointOfSale.API.Middleware;

public sealed class CookieCsrfProtectionMiddleware
{
    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;

    public CookieCsrfProtectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!UnsafeMethods.Contains(context.Request.Method) ||
            !AuthCookieConfiguration.HasAnyAuthenticationCookie(context.Request))
        {
            await _next(context);
            return;
        }

        if (AuthCookieConfiguration.IsValidCsrfToken(context.Request))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                message = "CSRF token validation failed.",
                code = "CSRF_VALIDATION_FAILED"
            });
    }
}
