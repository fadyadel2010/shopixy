using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;
using PointOfSale.API.Security;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Architecture;

public sealed class PosPermissionAuthorityCertificationTests
{
    [Fact]
    public void RoleProtectedEndpoints_MustUseSqlCurrentAdministrativeAuthority()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var middleware = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Middleware/AdminTenantAuthorityMiddleware.cs");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.API/Security/AdminTenantAuthority.cs");

        Assert.Contains("app.UseAuthentication();", program, StringComparison.Ordinal);
        Assert.Contains("app.UseAuthorization();", program, StringComparison.Ordinal);
        Assert.Contains("AdminTenantAuthorityMiddleware", program, StringComparison.Ordinal);
        Assert.Contains("ExtractTenantRoleSets(endpoint)", middleware, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Users", authority, StringComparison.Ordinal);
        Assert.Contains("current_actor_role_not_authorized", authority, StringComparison.Ordinal);
        Assert.Contains("requiredRoles.Contains(currentRole)", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void RepresentativeAdministrativeEndpoints_MustDeclareBackendPermissionPolicies()
    {
        // R2 remediation moved AuditLogsController from role strings to
        // permission policies; the commerce/affiliate controllers keep their
        // Owner,Admin role gates with the permission convention layered on
        // top (certified by PosEndpointPermissionManifestTests).
        AssertPermissionPolicy(typeof(AuditLogsController), PosPermissions.AuditView);
        AssertRoles(typeof(AdminAffiliateController), "Owner,Admin");
        AssertRoles(typeof(AdminCommerceHubController), "Owner,Admin");
        AssertRoles(typeof(AdminCommerceHubFinanceController), "Owner,Admin");
        AssertRoles(typeof(AdminCommerceHubPaymentsController), "Owner,Admin");
    }

    [Fact]
    public void FrontendPermissionGuards_MustRemainPresentationOnly()
    {
        var frontendGuard = CertificationSource.ReadRepoFile(
            "PointOfSale.Web/src/components/PermissionGuard.jsx");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        // Presentation guards consume server-derived permissions only.
        Assert.Contains("hasPosPermission", frontendGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("requiredRoles.includes(userRole)", frontendGuard, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessTokenAsync", program, StringComparison.Ordinal);
        Assert.Contains("AdminTenantAuthorityMiddleware", program, StringComparison.Ordinal);
    }

    private static void AssertPermissionPolicy(Type controllerType, string permission)
    {
        var authorize = controllerType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single(attribute =>
                !string.IsNullOrWhiteSpace(attribute.Policy) &&
                string.Equals(attribute.Policy, permission, StringComparison.Ordinal));

        Assert.Equal(permission, authorize.Policy);
    }

    private static void AssertRoles(Type controllerType, string roles)
    {
        var authorize = controllerType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single(attribute => !string.IsNullOrWhiteSpace(attribute.Roles));

        Assert.Equal(roles, authorize.Roles);
    }
}
