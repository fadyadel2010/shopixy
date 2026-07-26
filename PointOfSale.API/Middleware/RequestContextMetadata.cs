using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Middleware;

internal static class RequestContextMetadata
{
    public static string GetCorrelationId(HttpContext context)
    {
        return string.IsNullOrWhiteSpace(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;
    }

    public static string? GetTenantId(HttpContext context)
    {
        var tenantContext = context.RequestServices.GetService<ITenantContext>();
        if (tenantContext?.TenantId is Guid tenantId && tenantId != Guid.Empty)
        {
            return tenantId.ToString();
        }

        var tenantClaim = context.User.FindFirstValue("tenant_id")
            ?? context.User.FindFirstValue("TenantId");

        if (!string.IsNullOrWhiteSpace(tenantClaim))
        {
            return tenantClaim;
        }

        var headerTenantId = context.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName].ToString();
        return string.IsNullOrWhiteSpace(headerTenantId) ? null : headerTenantId;
    }

    public static string? GetUserId(HttpContext context) => GetUserId(context.User);

    public static string? GetUserId(ClaimsPrincipal? principal)
    {
        if (principal == null)
        {
            return null;
        }

        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue("UserId")
            ?? principal.FindFirstValue("CustomerId")
            ?? principal.FindFirstValue("affiliate_user_id");
    }
}
