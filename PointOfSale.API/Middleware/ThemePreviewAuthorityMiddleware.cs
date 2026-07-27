using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Middleware;

public sealed class ThemePreviewAuthorityMiddleware
{
    public const string TenantIdItemKey = "ThemePreview.TenantId";
    public const string ThemeIdItemKey = "ThemePreview.ThemeId";
    public const string TokenHeaderName = "X-Theme-Preview-Token";

    private static readonly PathString PreviewConfigPath = new("/api/storefront/theme-preview/config");

    private static readonly PathString[] PreviewReadPrefixes =
    [
        PreviewConfigPath,
        new PathString("/api/storefront/store-config"),
        new PathString("/api/storefront/home"),
        new PathString("/api/storefront/navigation"),
        new PathString("/api/storefront/pages"),
        new PathString("/api/storefront/products"),
        new PathString("/api/storefront/categories"),
        new PathString("/api/storefront/footer"),
        new PathString("/api/storefront/search"),
        new PathString("/api/storefront/seo"),
        new PathString("/api/storefront/sitemap"),
        new PathString("/api/storefront/resolve-slug"),
        new PathString("/api/storefront/smart-offers"),
        new PathString("/api/storefront/landing")
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<ThemePreviewAuthorityMiddleware> _logger;

    public ThemePreviewAuthorityMiddleware(
        RequestDelegate next,
        ILogger<ThemePreviewAuthorityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IStorefrontThemePreviewTokenService tokenService,
        TenantContext tenantContext,
        ApplicationDbContext dbContext)
    {
        var token = ResolvePreviewToken(context);
        var isPreviewConfigRequest = context.Request.Path.StartsWithSegments(
            PreviewConfigPath,
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(token))
        {
            if (isPreviewConfigRequest)
            {
                await RejectAsync(context, "THEME_PREVIEW_TOKEN_REQUIRED", "Theme preview token is required.");
                return;
            }

            await _next(context);
            return;
        }

        if (!IsAllowedPreviewRequest(context))
        {
            await RejectAsync(context, "THEME_PREVIEW_SCOPE_FORBIDDEN", "Theme preview tokens can only authorize read-only storefront preview requests.");
            return;
        }

        if (!tokenService.TryValidate(token, out var tenantId, out var themeId))
        {
            _logger.LogWarning(
                "Theme preview token rejected | Path: {Path}",
                context.Request.Path.Value ?? "unknown");

            await RejectAsync(context, "THEME_PREVIEW_INVALID", "Theme preview capability is invalid or expired.");
            return;
        }

        var tenantIsActive = await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null,
                context.RequestAborted);

        if (!tenantIsActive)
        {
            _logger.LogWarning(
                "Theme preview token rejected because tenant is not active | TenantId: {TenantId} | Path: {Path}",
                tenantId,
                context.Request.Path.Value ?? "unknown");

            await RejectAsync(context, "THEME_PREVIEW_TENANT_UNAVAILABLE", "Theme preview tenant is not available.");
            return;
        }

        tenantContext.SetTenantId(tenantId);
        context.Items[TenantIdItemKey] = tenantId;
        context.Items[ThemeIdItemKey] = themeId;

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("theme_preview", "true")
        ], "ThemePreview"));

        await _next(context);
    }

    private static string? ResolvePreviewToken(HttpContext context)
    {
        var headerToken = context.Request.Headers[TokenHeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(headerToken))
        {
            return headerToken.Trim();
        }

        var queryToken = context.Request.Query["themePreviewToken"].ToString();
        return string.IsNullOrWhiteSpace(queryToken) ? null : queryToken.Trim();
    }

    private static bool IsAllowedPreviewRequest(HttpContext context)
    {
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
        {
            return false;
        }

        return PreviewReadPrefixes.Any(prefix =>
            context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task RejectAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(code, message, context.TraceIdentifier),
            context.RequestAborted);
    }
}
