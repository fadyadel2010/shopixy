using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Security;

public sealed class AdminRouteAuthorizationHardeningTests
{
    private static readonly Assembly ApiAssembly = typeof(OrdersController).Assembly;
    private static readonly HashSet<string> ApprovedAnonymousAdminActions = new(StringComparer.Ordinal)
    {
        "AdminAbandonedCartRecoveryController.LogRecoveryResult"
    };

    [Fact]
    public void WhatsAppAdminController_MustRequireOwnerAdminManagerRoles()
    {
        var authorize = typeof(WhatsAppAdminController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);

        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin,Manager", authorize!.Roles);
    }

    [Fact]
    public void ApiAdminControllers_MustNotUseBareAuthorizeAttributes()
    {
        var failures = new List<string>();

        foreach (var controllerType in ApiAssembly.GetTypes().Where(IsApiAdminController))
        {
            ValidateAuthorizeAttributes(controllerType, controllerType, failures);

            foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                ValidateAuthorizeAttributes(controllerType, method, failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void WhatsAppAdminController_DeniesCashier_AndAllowsAdmin()
    {
        var authorize = typeof(WhatsAppAdminController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);

        var cashier = BuildPrincipal("Cashier");
        var admin = BuildPrincipal("Admin");

        Assert.False(IsAuthorized(cashier, authorize!));
        Assert.True(IsAuthorized(admin, authorize!));
    }

    [Fact]
    public void ApprovedAnonymousAdminActions_MustDeclareAlternativeMachineAuthBoundary()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "Admin", "AdminAbandonedCartRecoveryController.cs"));

        Assert.Contains("[AllowAnonymous]", source, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]", source, StringComparison.Ordinal);
        Assert.Contains("Accepts either an authenticated admin principal or a valid callback API key.", source, StringComparison.Ordinal);
        Assert.Contains("ResolveRecoveryApiKey", source, StringComparison.Ordinal);
        Assert.Contains("IsValidApiKey", source, StringComparison.Ordinal);
        Assert.Contains("N8nWebhookApiKey", source, StringComparison.Ordinal);
    }

    private static bool IsApiAdminController(Type controllerType)
    {
        if (!controllerType.Name.EndsWith("Controller", StringComparison.Ordinal))
        {
            return false;
        }

        return controllerType.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Any(route => route.Template != null && route.Template.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateAuthorizeAttributes(MemberInfo controllerType, MemberInfo member, List<string> failures)
    {
        // Check for [AllowAnonymous] on admin routes - this bypasses class-level authorization
        var allowAnonymousOnMethod = member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
        if (allowAnonymousOnMethod && !IsApprovedAnonymousAdminAction(controllerType, member))
        {
            failures.Add($"{controllerType.Name}.{member.Name} uses [AllowAnonymous] on an /api/admin route.");
        }

        // Check for bare [Authorize] without roles, policy, or authentication schemes
        foreach (var authorize in member.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
        {
            if (string.IsNullOrWhiteSpace(authorize.Roles) &&
                string.IsNullOrWhiteSpace(authorize.Policy) &&
                string.IsNullOrWhiteSpace(authorize.AuthenticationSchemes))
            {
                failures.Add($"{controllerType.Name}.{member.Name} uses bare [Authorize] on an /api/admin route.");
            }
        }
    }

    private static ClaimsPrincipal BuildPrincipal(string role)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "TestAuth"));
    }

    private static bool IsAuthorized(ClaimsPrincipal principal, AuthorizeAttribute authorize)
    {
        var allowedRoles = (authorize.Roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return allowedRoles.Any(principal.IsInRole);
    }

    private static bool IsApprovedAnonymousAdminAction(MemberInfo controllerType, MemberInfo member) =>
        ApprovedAnonymousAdminActions.Contains($"{controllerType.Name}.{member.Name}");

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));
}
