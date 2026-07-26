using System.Security.Claims;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public static class SuperAdminTokenClaimsValidator
{
    public static bool HasRequiredClaims(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role");
        var authType = principal.FindFirstValue(SuperAdminSecurityConstants.AuthTypeClaim);
        var superAdminId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var tokenVersion = principal.FindFirstValue(SuperAdminSecurityConstants.TokenVersionClaim);

        return string.Equals(role, SuperAdminSecurityConstants.Role, StringComparison.Ordinal) &&
               string.Equals(authType, SuperAdminSecurityConstants.AuthTypeValue, StringComparison.Ordinal) &&
               Guid.TryParse(superAdminId, out _) &&
               int.TryParse(tokenVersion, out _);
    }
}
