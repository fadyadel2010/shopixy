using Microsoft.Net.Http.Headers;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Canonical HTTP cache contract for every Storefront API response.
/// Storefront data is tenant-, publication-, inventory-, pricing-, or identity-sensitive,
/// so intermediaries and browser HTTP caches must not retain it. Application-owned
/// browser runtime caches remain responsible for their explicitly certified entries.
/// Validators are deliberately not emitted or honored by this contract.
/// </summary>
public sealed class StorefrontHttpCachePolicyMiddleware
{
    public const string CacheControlValue = "private, no-store, no-cache, max-age=0, must-revalidate";

    private readonly RequestDelegate _next;

    public StorefrontHttpCachePolicyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/storefront", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        ApplyContract(context.Response.Headers);
        context.Response.OnStarting(static state =>
        {
            ApplyContract(((HttpContext)state).Response.Headers);
            return Task.CompletedTask;
        }, context);

        await _next(context);

        // Also normalize late headers for in-memory hosts and for handlers that
        // completed without starting the response. OnStarting remains the final
        // authority in the real server response pipeline.
        if (!context.Response.HasStarted)
        {
            ApplyContract(context.Response.Headers);
        }
    }

    private static void ApplyContract(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = CacheControlValue;
        headers[HeaderNames.Pragma] = "no-cache";
        headers[HeaderNames.Expires] = "0";
        headers["Surrogate-Control"] = "no-store";
        headers.Remove(HeaderNames.ETag);
        headers.Remove(HeaderNames.LastModified);
    }
}
