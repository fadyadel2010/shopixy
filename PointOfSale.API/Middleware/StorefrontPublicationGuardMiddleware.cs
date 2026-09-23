using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Middleware;

public sealed class StorefrontPublicationGuardMiddleware
{
    private static readonly PathString[] GuardedPrefixes =
    [
        new PathString("/api/storefront/store-config"),
        new PathString("/api/storefront/home"),
        new PathString("/api/storefront/navigation"),
        new PathString("/api/storefront/pages"),
        new PathString("/api/storefront/contact"),
        new PathString("/api/storefront/products"),
        new PathString("/api/storefront/categories"),
        new PathString("/api/storefront/footer"),
        new PathString("/api/storefront/search"),
        new PathString("/api/storefront/payment-methods"),
        new PathString("/api/storefront/seo"),
        new PathString("/api/storefront/sitemap"),
        new PathString("/api/storefront/resolve-slug"),
        new PathString("/api/storefront/smart-offers"),
        new PathString("/api/storefront/landing")
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<StorefrontPublicationGuardMiddleware> _logger;

    public StorefrontPublicationGuardMiddleware(
        RequestDelegate next,
        ILogger<StorefrontPublicationGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldGuardRequest(context))
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (tenantContext.TenantId is not Guid tenantId || tenantId == Guid.Empty)
        {
            await _next(context);
            return;
        }

        var publicationStatusService = context.RequestServices.GetRequiredService<IStorefrontPublicationStatusService>();
        if (await publicationStatusService.IsPublishedAsync(tenantId, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        _logger.LogInformation(
            "Blocked public storefront request for unpublished tenant | TenantId: {TenantId} | Path: {Path} | TraceId: {TraceId}",
            tenantId,
            context.Request.Path.Value ?? "unknown",
            traceId);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse("STOREFRONT_NOT_PUBLISHED", "Storefront not found.", traceId),
            context.RequestAborted);
    }

    private static bool ShouldGuardRequest(HttpContext context)
    {
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
        {
            return false;
        }

        // Store config is the bootstrap authority for live, maintenance, and
        // disabled storefront screens. Its controller still rejects inactive
        // tenants, while this guard blocks all other unpublished public reads.
        if (context.Request.Path.Equals("/api/storefront/store-config", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.Equals("/api/storefront/store-config/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() == null)
        {
            return false;
        }

        return GuardedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
