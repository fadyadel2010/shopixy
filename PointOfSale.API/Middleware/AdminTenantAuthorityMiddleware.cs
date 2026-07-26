using PointOfSale.API.Security;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Revalidates tenant-bound privileged endpoints against current SQL actor authority.
/// This collapses stale admin JWT role/session replay after user deactivation, role
/// downgrade, tenant movement, or tenant deactivation.
/// </summary>
public sealed class AdminTenantAuthorityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AdminTenantAuthorityMiddleware> _logger;

    public AdminTenantAuthorityMiddleware(
        RequestDelegate next,
        ILogger<AdminTenantAuthorityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAdminTenantAuthority adminTenantAuthority,
        ITenantContext tenantContext)
    {
        var endpoint = context.GetEndpoint();
        if (AdminTenantAuthority.HasSuperAdminPolicy(endpoint))
        {
            await _next(context);
            return;
        }

        var requiredRoleSets = AdminTenantAuthority.ExtractTenantRoleSets(endpoint);
        if (requiredRoleSets.Count == 0)
        {
            await _next(context);
            return;
        }

        var result = await adminTenantAuthority.ValidateTenantActorAsync(
            context.User,
            tenantContext.TenantId,
            requiredRoleSets,
            context.RequestAborted);

        if (result.Authorized)
        {
            context.Items["AdminTenantAuthority.UserId"] = result.ActorUserId;
            context.Items["AdminTenantAuthority.TenantId"] = result.TenantId;
            context.Items["AdminTenantAuthority.Role"] = result.CurrentRole;
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Tenant admin authority rejected request | Reason: {Reason} | Path: {Path} | ActorUserId: {ActorUserId} | TenantId: {TenantId} | CurrentRole: {CurrentRole}",
            result.Reason,
            context.Request.Path.Value,
            result.ActorUserId,
            result.TenantId,
            result.CurrentRole ?? "(unknown)");

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Current actor is not authorized for this tenant operation.",
            code = "ADMIN_SCOPE_FORBIDDEN",
            reason = result.Reason
        }, context.RequestAborted);
    }
}
