using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Security;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

public sealed class PosPermissionAuthorizationTests
{
    [Fact]
    public void Catalog_HasUniqueKnownPermissionsAndEveryRoleHasExplicitGrants()
    {
        Assert.Equal(PosPermissions.All.Count, PosPermissions.All.Distinct(StringComparer.Ordinal).Count());

        foreach (var role in Enum.GetValues<UserRole>())
        {
            var grants = PosRolePermissionCatalog.GetPermissions(role);
            Assert.All(grants, permission => Assert.True(PosPermissions.All.Contains(permission), permission));
        }

        Assert.True(PosRolePermissionCatalog.IsGranted(UserRole.Owner, PosPermissions.UsersOwnershipTransfer));
        Assert.False(PosRolePermissionCatalog.IsGranted(UserRole.Admin, PosPermissions.UsersOwnershipTransfer));
        Assert.False(PosRolePermissionCatalog.IsGranted(UserRole.Manager, PosPermissions.ReportsFinancialView));
        Assert.False(PosRolePermissionCatalog.IsGranted(UserRole.Cashier, PosPermissions.FinanceView));
        Assert.True(PosRolePermissionCatalog.IsGranted(UserRole.Warehouse, PosPermissions.InventoryTransfer));
    }

    [Theory]
    [InlineData(UserRole.Cashier, "finance.admin", false)]
    [InlineData(UserRole.Warehouse, "purchases.manage", false)]
    [InlineData(UserRole.Manager, "users.view", false)]
    [InlineData(UserRole.Admin, "users.ownership.transfer", false)]
    [InlineData(UserRole.Manager, "finance.operate", true)]
    [InlineData(UserRole.Warehouse, "shipping.fulfill", true)]
    [InlineData(UserRole.Cashier, "sales.operate", true)]
    [InlineData(UserRole.Cashier, "dashboard.view", true)]
    [InlineData(UserRole.Cashier, "settings.view", false)]
    public async Task Handler_EnforcesRoleMatrix(UserRole role, string permission, bool expected)
    {
        var result = await AuthorizeAsync(PosPrincipal(role), permission);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Cashier, false)]
    [InlineData(UserRole.Warehouse, true)]
    public async Task WarehouseInventoryList_UsesCanonicalInventoryViewMatrix(UserRole role, bool expected)
    {
        Assert.True(PosEndpointPermissionCatalog.TryResolve(
            "Warehouses", "GetWarehouses", ["GET"], out var permission));
        Assert.Equal(PosPermissions.InventoryView, permission);
        Assert.Equal(expected, await AuthorizeAsync(PosPrincipal(role), permission));
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Cashier, true)]
    [InlineData(UserRole.Warehouse, false)]
    public async Task PosWarehouseOptions_UseCanonicalSalesOperateMatrix(UserRole role, bool expected)
    {
        Assert.True(PosEndpointPermissionCatalog.TryResolve(
            "PosCarts", "GetWarehouseOptions", ["GET"], out var permission));
        Assert.Equal(PosPermissions.SalesOperate, permission);
        Assert.Equal(expected, await AuthorizeAsync(PosPrincipal(role), permission));
    }

    [Fact]
    public async Task Handler_DeniesUnknownPermissionUnknownRoleAndMissingRole()
    {
        Assert.False(await AuthorizeAsync(PosPrincipal(UserRole.Owner), "unknown.permission"));
        Assert.False(await AuthorizeAsync(Principal(Role("Root"), PosAuthority()), PosPermissions.DashboardView));
        Assert.False(await AuthorizeAsync(Principal(PosAuthority()), PosPermissions.DashboardView));
    }

    [Fact]
    public async Task Handler_IgnoresForgedPermissionClaims()
    {
        var principal = PosPrincipal(
            UserRole.Cashier,
            new Claim("permission", PosPermissions.FinanceAdmin),
            new Claim("permissions", PosPermissions.UsersOwnershipTransfer));

        Assert.False(await AuthorizeAsync(principal, PosPermissions.FinanceAdmin));
    }

    [Fact]
    public async Task Handler_DeniesForgedRoleWithoutSqlAuthorityMarker()
    {
        var principal = Principal(
            Role(nameof(UserRole.Owner)),
            UserId(), TenantId(), Session());

        Assert.False(await AuthorizeAsync(principal, PosPermissions.UsersOwnershipTransfer));
    }

    [Fact]
    public async Task Handler_DeniesMalformedOrNonPosPrincipals()
    {
        Assert.False(await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), PosPermissions.DashboardView));
        Assert.False(await AuthorizeAsync(
            Principal(new Claim(ClaimTypes.Role, "SuperAdmin"), new Claim("auth_type", "super_admin")),
            PosPermissions.DashboardView));
        Assert.False(await AuthorizeAsync(
            Principal(Role(nameof(UserRole.Owner)), PosAuthority(), UserId(), TenantId()),
            PosPermissions.DashboardView));
        Assert.False(await AuthorizeAsync(
            Principal(Role(nameof(UserRole.Owner)), Role(nameof(UserRole.Admin)), PosAuthority(), UserId(), TenantId(), Session()),
            PosPermissions.DashboardView));
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal principal, string permission)
    {
        var requirement = new PosPermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        await new PosPermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal PosPrincipal(UserRole role, params Claim[] extraClaims) =>
        Principal([Role(role.ToString()), PosAuthority(), UserId(), TenantId(), Session(), .. extraClaims]);

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Bearer"));

    private static Claim Role(string role) => new(ClaimTypes.Role, role);
    private static Claim PosAuthority() => new(PosSecurityConstants.SqlAuthorityClaim, PosSecurityConstants.SqlAuthorityValue);
    private static Claim UserId() => new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString());
    private static Claim TenantId() => new("tenant_id", Guid.NewGuid().ToString());
    private static Claim Session() => new(PosSecurityConstants.SessionBindingClaim, "current-session-binding");
}
