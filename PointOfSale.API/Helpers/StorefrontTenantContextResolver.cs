using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Helpers;

public static class StorefrontTenantContextResolver
{
    public static (Guid? TenantId, IActionResult? Error) ValidateTenant(
        ControllerBase controller,
        string traceId)
    {
        var tenantId = GetTenantId(controller.HttpContext);
        if (!tenantId.HasValue)
        {
            return (null, controller.BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId)));
        }

        return (tenantId.Value, null);
    }

    public static Guid? GetTenantId(HttpContext httpContext)
    {
        var tenantContext = httpContext.RequestServices?.GetService<ITenantContext>();
        if (tenantContext?.TenantId is Guid tenantId && tenantId != Guid.Empty)
        {
            return tenantId;
        }

        return null;
    }
}
