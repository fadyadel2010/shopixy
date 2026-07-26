namespace PointOfSale.Tests.Architecture;

public sealed class SuperAdminPlatformAuthorityCertificationTests
{
    [Fact]
    public void SuperAdminAccessTokens_MustBeBoundToCurrentSqlPlatformAuthority()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Services", "SuperAdminAuthService.cs"),
            "SuperAdminSecurityConstants.TokenVersionClaim",
            "superAdmin.TokenVersion.ToString()");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminTokenClaimsValidator.cs"),
            "TokenVersionClaim",
            "Guid.TryParse(superAdminId",
            "int.TryParse(tokenVersion");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthority.cs"),
            "JWT claims identify the candidate actor only",
            "_dbContext.SuperAdmins",
            "_dbContext.UserGovernanceProfiles",
            "STALE_SUPER_ADMIN_TOKEN",
            "SUPER_ADMIN_GOVERNANCE_REJECTED",
            "OperationalConvergenceTelemetry.RecordAuthorityFailure",
            "OperationalConvergenceAuthority.PlatformAdmin");
    }

    [Fact]
    public void SuperAdminPlatformAuthorityMiddleware_MustGuardEverySuperAdminPolicyEndpoint()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthorityMiddleware.cs"),
            "GetOrderedMetadata<IAuthorizeData>()",
            "SuperAdminSecurityConstants.PolicyName",
            "ValidatePlatformActorAsync",
            "SUPER_ADMIN_SCOPE_FORBIDDEN",
            "SuperAdminPlatformAuthority.SuperAdminId");

        var program = File.ReadAllText(ApiPath("Program.cs"));
        Assert.Contains("AddScoped<PointOfSale.API.SuperAdmin.Infrastructure.ISuperAdminPlatformAuthority", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminPlatformAuthorityMiddleware>()", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal) <
            program.IndexOf("SuperAdminPlatformAuthorityMiddleware", StringComparison.Ordinal),
            "Platform authority middleware must run after authorization has selected the endpoint policy.");

        AssertSourceContains(
            ApiPath("Controllers", "AdminAffiliateCategoryController.cs"),
            "SuperAdminSecurityConstants.PolicyName",
            "[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]");
    }

    [Fact]
    public void SuperAdminCertificationDocumentation_MustRecordFindingsAndRemainingDebt()
    {
        var certification = File.ReadAllText(DocsPath("architecture", "super-admin-platform-authority-certification-findings.md"));

        foreach (var marker in new[]
        {
            "SUPERADMIN-CON-01",
            "Boundaries Audited",
            "Dangerous Global Primitives Reviewed",
            "Runtime Hardening Added",
            "Replay And Rebuild Findings",
            "Operational Auditability Findings",
            "Remaining Platform-Authority Debt",
            "stale super-admin access-token reuse",
            "global/all-tenant repair remains intentionally flexible"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        AssertSourceContains(
            DocsPath("architecture", "dangerous-operational-primitives.md"),
            "SUPERADMIN-CON-01",
            "Super-admin access tokens are candidate evidence only");

        AssertSourceContains(
            DocsPath("operations", "operational-recovery-runbooks.md"),
            "## Platform Authority And Global Operations",
            "global/all-tenant operation");
    }

    private static void AssertSourceContains(string path, params string[] markers)
    {
        var source = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
