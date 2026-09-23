using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PointOfSale.API.Controllers;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Architecture;

public sealed class PosAdministrativeAuthorityCertificationTests
{
    [Fact]
    public void OrdinaryStaffRegistration_MustBeDisabledAndPermissionProtected()
    {
        Assert.Null(typeof(RegisterRequest).GetProperty("Role"));
        Assert.Null(typeof(RegisterRequest).GetProperty("TenantId"));

        var authController = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/AuthController.cs");

        Assert.Contains("[Authorize(Policy = PosPermissions.UsersInvite)]", authController, StringComparison.Ordinal);
        Assert.Contains("POS_STAFF_REGISTRATION_DISABLED", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("Role = UserRole.Cashier", authController, StringComparison.Ordinal);
        Assert.Contains("Property 'role' is not allowed.", authController, StringComparison.Ordinal);
        Assert.Contains("Property 'tenantId' is not allowed.", authController, StringComparison.Ordinal);
    }

    [Fact]
    public void PosRuntime_MustNotExposeRoleAssignmentOrSelfPromotionEndpoint()
    {
        var apiAssembly = typeof(AuthController).Assembly;
        var failures = apiAssembly.GetTypes()
            .Where(type => type.IsClass && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Where(method =>
                method.Name.Contains("AssignRole", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("ChangeRole", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("PromoteUser", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("UpdateUserRole", StringComparison.OrdinalIgnoreCase))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void OwnerAndTestAdminCreation_MustRemainRestrictedBootstrapPaths()
    {
        var onboarding = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Platform/TenantRegistrationService.cs");
        var testAuth = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Controllers/TestAuthController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("Role = UserRole.Owner", onboarding, StringComparison.Ordinal);
        Assert.Contains("Role = UserRole.Admin", testAuth, StringComparison.Ordinal);
        Assert.Contains("TestingOnlyControllerFeatureProvider", program, StringComparison.Ordinal);
        Assert.Contains("isTestingEnvironment", program, StringComparison.Ordinal);
        Assert.Contains("manager.FeatureProviders.Add(new TestingOnlyControllerFeatureProvider", program, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectApiInvocation_MustStillCrossSignedJwtAndSqlAuthority()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("ValidateIssuerSigningKey = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime = true", program, StringComparison.Ordinal);
        Assert.Contains("ClockSkew = TimeSpan.Zero", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IPosSessionAuthority>()", program, StringComparison.Ordinal);
    }
}
