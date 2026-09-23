using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.Core.Security;

namespace PointOfSale.API.Security;

public sealed class PosPermissionAuthorizationHandler
    : AuthorizationHandler<PosPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PosPermissionRequirement requirement)
    {
        if (!PosPermissions.All.Contains(requirement.Permission) ||
            context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var roleClaims = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = context.User.FindFirstValue("tenant_id");
        var sessionBinding = context.User.FindFirstValue(PosSecurityConstants.SessionBindingClaim);
        var sqlAuthorityClaims = context.User.FindAll(PosSecurityConstants.SqlAuthorityClaim).ToArray();

        // These claims are minted only by the POS bearer issuer and are revalidated
        // against current SQL state by PosSessionAuthority before authorization.
        if (roleClaims.Length != 1 ||
            sqlAuthorityClaims.Length != 1 ||
            !string.Equals(sqlAuthorityClaims[0].Value, PosSecurityConstants.SqlAuthorityValue, StringComparison.Ordinal) ||
            !Guid.TryParse(userId, out var parsedUserId) || parsedUserId == Guid.Empty ||
            !Guid.TryParse(tenantId, out var parsedTenantId) || parsedTenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(sessionBinding) ||
            !PosRolePermissionCatalog.TryParseKnownRole(roleClaims[0], out var role))
        {
            return Task.CompletedTask;
        }

        if (PosRolePermissionCatalog.IsGranted(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
