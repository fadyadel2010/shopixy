using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using PointOfSale.API.Controllers;
using PointOfSale.API.Security;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

public sealed class PosEndpointPermissionManifestTests
{
    private static readonly Assembly ApiAssembly = typeof(BanksController).Assembly;

    private static readonly IReadOnlyDictionary<string, string> ExplicitExemptControllers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthController"] = "POS login, refresh, logout, email verification, and current-session ceremonies.",
            ["MarketplaceController"] = "Services-marketplace actor and resource authority is enforced by its dedicated services.",
        };

    [Fact]
    public void SensitivePosActions_MustResolveToKnownPermissionPolicies()
    {
        var failures = new List<string>();
        var seenClassifiedControllers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var controller in ApiAssembly.GetTypes()
                     .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Controller", StringComparison.Ordinal)))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()))
            {
                var endpoint = $"{controller.Name}.{action.Name}";
                var attributes = controller.GetCustomAttributes(inherit: true)
                    .Concat(action.GetCustomAttributes(inherit: true))
                    .ToArray();

                if (attributes.OfType<IAllowAnonymous>().Any())
                {
                    if (endpoint == "AuthController.Register")
                    {
                        failures.Add($"{endpoint} must never be anonymous.");
                    }

                    continue;
                }

                var authorizeData = attributes.OfType<IAuthorizeData>().ToArray();
                if (HasPermissionPolicy(authorizeData))
                {
                    seenClassifiedControllers.Add(controller.Name[..^"Controller".Length]);
                    continue;
                }

                if (HasApprovedSeparateAuthority(controller, authorizeData))
                {
                    continue;
                }

                var httpMethods = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .SelectMany(attribute => attribute.HttpMethods)
                    .Select(method => method.ToUpperInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (PosEndpointPermissionCatalog.TryResolve(
                        controller.Name[..^"Controller".Length],
                        action.Name,
                        httpMethods,
                        out var permission))
                {
                    seenClassifiedControllers.Add(controller.Name[..^"Controller".Length]);
                    if (!PosPermissions.All.Contains(permission))
                    {
                        failures.Add($"{endpoint} resolves unknown permission '{permission}'.");
                    }

                    continue;
                }

                if (ExplicitExemptControllers.ContainsKey(controller.Name))
                {
                    continue;
                }

                failures.Add($"{endpoint} is authenticated but has no POS permission or documented separate authority.");
            }
        }

        foreach (var controllerName in PosEndpointPermissionCatalog.ClassifiedControllers)
        {
            if (!seenClassifiedControllers.Contains(controllerName))
            {
                failures.Add($"Permission manifest entry '{controllerName}' has no classified HTTP action.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.OrderBy(value => value, StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("Banks", "GetBanks", "GET", PosPermissions.FinanceView)]
    [InlineData("Banks", "CreateBank", "POST", PosPermissions.FinanceAdmin)]
    [InlineData("FinancialAccounts", "Transfer", "POST", PosPermissions.FinanceOperate)]
    [InlineData("Purchases", "CreatePurchase", "POST", PosPermissions.PurchasesManage)]
    [InlineData("Sales", "CreateSale", "POST", PosPermissions.SalesOperate)]
    [InlineData("Settings", "GetSettings", "GET", PosPermissions.DashboardView)]
    [InlineData("Settings", "UpdateSettings", "PUT", PosPermissions.SettingsManage)]
    [InlineData("FinancialReports", "GetTrialBalance", "GET", PosPermissions.ReportsFinancialView)]
    [InlineData("Warehouses", "GetWarehouses", "GET", PosPermissions.InventoryView)]
    [InlineData("Warehouses", "CreateWarehouse", "POST", PosPermissions.InventoryApprove)]
    [InlineData("Warehouses", "AdjustStock", "POST", PosPermissions.InventoryAdjust)]
    [InlineData("Warehouses", "TransferStock", "POST", PosPermissions.InventoryTransfer)]
    [InlineData("PosCarts", "GetWarehouseOptions", "GET", PosPermissions.SalesOperate)]
    public void RepresentativeNonAdminRoutes_AreInThePermissionManifest(
        string controller,
        string action,
        string method,
        string expectedPermission)
    {
        Assert.True(PosEndpointPermissionCatalog.TryResolve(controller, action, [method], out var permission));
        Assert.Equal(expectedPermission, permission);
    }

    [Fact]
    public void Runtime_MustInstallPermissionConventionAndSchemeBoundPolicies()
    {
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("PosPermissionApplicationModelConvention", program, StringComparison.Ordinal);
        Assert.Contains("policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme)", program, StringComparison.Ordinal);
        Assert.Contains("new PosPermissionRequirement(permission)", program, StringComparison.Ordinal);
        Assert.Contains("PosPermissionAuthorizationHandler", program, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRoleAttributes_MustNotNarrowCanonicalPermissionGrants()
    {
        var failures = new List<string>();

        foreach (var controller in ApiAssembly.GetTypes()
                     .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Controller", StringComparison.Ordinal)))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()))
            {
                var httpMethods = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .SelectMany(attribute => attribute.HttpMethods)
                    .Select(method => method.ToUpperInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (!PosEndpointPermissionCatalog.TryResolve(
                        controller.Name[..^"Controller".Length],
                        action.Name,
                        httpMethods,
                        out var permission))
                {
                    continue;
                }

                var roleAttributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                    .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Roles))
                    .ToArray();

                foreach (var roleAttribute in roleAttributes)
                {
                    var admittedRoles = roleAttribute.Roles!
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);

                    foreach (var role in Enum.GetValues<UserRole>()
                                 .Where(role => PosRolePermissionCatalog.IsGranted(role, permission)))
                    {
                        if (!admittedRoles.Contains(role.ToString()))
                        {
                            failures.Add(
                                $"{controller.Name}.{action.Name}: permission '{permission}' grants {role}, " +
                                $"but legacy roles '{roleAttribute.Roles}' deny it.");
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static bool HasPermissionPolicy(IEnumerable<IAuthorizeData> attributes) =>
        attributes.Any(attribute =>
            !string.IsNullOrWhiteSpace(attribute.Policy) &&
            PosPermissions.All.Contains(attribute.Policy));

    private static bool HasApprovedSeparateAuthority(Type controller, IEnumerable<IAuthorizeData> attributes)
    {
        if (controller.Namespace?.Contains(".SuperAdmin.", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return attributes.Any(attribute =>
            !string.IsNullOrWhiteSpace(attribute.AuthenticationSchemes) ||
            (!string.IsNullOrWhiteSpace(attribute.Policy) && !PosPermissions.All.Contains(attribute.Policy)));
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));
}
