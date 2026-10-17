using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Controllers;
using PointOfSale.API.Security;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

public sealed class PosAdminQualityAuthorizationTests
{
    [Theory]
    [InlineData(nameof(AdminBostaDestinationsController.References), "GET", PosPermissions.ShippingManage)]
    [InlineData(nameof(AdminBostaDestinationsController.Validate), "POST", PosPermissions.ShippingManage)]
    [InlineData(nameof(AdminBostaDestinationsController.Read), "GET", PosPermissions.ShippingManage)]
    [InlineData(nameof(AdminBostaDestinationsController.Save), "PUT", PosPermissions.ShippingFulfill)]
    public void BostaNativeEndpointsRetainManagementAndExplicitShipmentWritePermissions(string action, string verb, string permission)
    {
        var type = typeof(AdminBostaDestinationsController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == PosPermissions.ShippingManage);
        Assert.True(PosEndpointPermissionCatalog.TryResolve("AdminBostaDestinations", action, [verb], out var actual));
        Assert.Equal(permission, actual);
        if (action == nameof(AdminBostaDestinationsController.Save))
            Assert.Equal(PosPermissions.ShippingFulfill, type.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.False(PosRolePermissionCatalog.IsGranted(UserRole.Cashier, permission));
    }
    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, false)]
    [InlineData(UserRole.Cashier, false)]
    [InlineData(UserRole.Warehouse, false)]
    public async Task CheckoutAttachments_RequireDistinctPermission_AndPreserveOwnerAdminRestriction(UserRole role, bool expected)
    {
        var method = typeof(AdminOrdersController).GetMethod(nameof(AdminOrdersController.DownloadCheckoutAttachment))!;
        var roles = method.GetCustomAttribute<AuthorizeAttribute>()!.Roles;
        Assert.Equal("Owner,Admin", roles);
        Assert.True(PosEndpointPermissionCatalog.TryResolve("AdminOrders", method.Name, ["GET"], out var permission));
        Assert.Equal(PosPermissions.StorefrontOrderAttachmentsDownload, permission);
        Assert.Equal(permission, method.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(expected, PosRolePermissionCatalog.IsGranted(role, permission));
        var services = new ServiceCollection().AddLogging().AddAuthorization(options =>
            options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PosPermissionRequirement(permission))));
        services.AddSingleton<IAuthorizationHandler, PosPermissionAuthorizationHandler>();
        await using var provider = services.BuildServiceProvider();
        var policy = new AuthorizationPolicyBuilder().RequireRole(roles!.Split(','))
            .AddRequirements(new PosPermissionRequirement(permission)).Build();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim(PosSecurityConstants.SessionBindingClaim, "synthetic-binding"),
            new Claim(PosSecurityConstants.SqlAuthorityClaim, PosSecurityConstants.SqlAuthorityValue)
        ], "synthetic-pos"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy);
        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(AdminShipmentsController.GetParcelEvidence), PosPermissions.ShippingView)]
    [InlineData(nameof(AdminShipmentsController.SaveParcelEvidence), PosPermissions.ShippingFulfill)]
    [InlineData(nameof(AdminShipmentsController.QuoteParcel), PosPermissions.ShippingFulfill)]
    public void ParcelEndpoints_DeclareTheSamePermissionAsTheRuntimeConvention(string action, string permission)
    {
        var attribute = typeof(AdminShipmentsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal(permission, attribute.Policy);
    }
}
