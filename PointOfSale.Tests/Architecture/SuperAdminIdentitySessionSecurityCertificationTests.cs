namespace PointOfSale.Tests.Architecture;

public sealed class SuperAdminIdentitySessionSecurityCertificationTests
{
    [Fact]
    public void SuperAdminIdentityAuthority_MustRemainSqlTokenVersionAndGovernanceAuthoritative()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthority.cs"),
            "_dbContext.SuperAdmins",
            "_dbContext.UserGovernanceProfiles",
            "STALE_SUPER_ADMIN_TOKEN",
            "SUPER_ADMIN_GOVERNANCE_REJECTED",
            "OperationalConvergenceTelemetry.RecordAuthorityFailure");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Services", "SuperAdminAuthService.cs"),
            "EnsureAdminLoginAllowedAsync(superAdmin.Id",
            "superAdmin.SetRefreshToken(HashRefreshToken(refreshToken)",
            "superAdmin.ClearRefreshToken()",
            "TOKEN_REUSE_DETECTED",
            "SuperAdminSecurityConstants.TokenVersionClaim");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Services", "SuperAdminManagementService.cs"),
            "superAdmin.UpdatePasswordHash",
            "superAdmin.ClearRefreshToken()");

        AssertSourceContains(
            ApiPath("Services", "Governance", "UserGovernanceService.cs"),
            "subject.Role == UserGovernanceRole.Admin",
            "platformActor?.ClearRefreshToken()");
    }

    [Fact]
    public void SuperAdminMiddleware_MustHonorAnonymousAuthEndpointsAndAuditOnlyPlatformOperations()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthorityMiddleware.cs"),
            "GetMetadata<IAllowAnonymous>()",
            "ValidatePlatformActorAsync",
            "SUPER_ADMIN_SCOPE_FORBIDDEN");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Infrastructure", "SuperAdminOperationalAuditMiddleware.cs"),
            "GetMetadata<IAllowAnonymous>()",
            "PLATFORM_OPERATION_COMPLETED",
            "SuperAdminPlatformAuthority.SuperAdminId");
    }

    [Fact]
    public void LegacyPlatformEndpoints_MustNotUseTenantRoleStringsForPlatformAuthority()
    {
        var marketplace = File.ReadAllText(ApiPath("Controllers", "MarketplaceController.cs"));
        var manualWallet = File.ReadAllText(ApiPath("Controllers", "AdminManualPaymentController.cs"));

        Assert.Contains("ReindexServicesGlobally", marketplace, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]", marketplace, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Admin\")]", marketplace, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId", marketplace, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted", marketplace, StringComparison.Ordinal);
        Assert.Contains("s.IsActive", marketplace, StringComparison.Ordinal);
        Assert.Contains("s.Profile.IsActive", marketplace, StringComparison.Ordinal);
        Assert.DoesNotContain("User.IsInRole(\"SuperAdmin\")", marketplace, StringComparison.Ordinal);
        Assert.DoesNotContain("Roles = \"Admin,SuperAdmin\"", marketplace, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperAdmin\")]", manualWallet, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass111Documentation_MustRecordCertificationAndFreeze()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "super-admin-identity-session-security-certification-findings.md"));
        var projectContext = File.ReadAllText(ContextPath("PROJECT_CONTEXT.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var knownRisks = File.ReadAllText(ContextPath("KNOWN_RISKS.md"));
        var rules = File.ReadAllText(ContextPath("CODEX_RULES.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 11.1",
            "Current SQL SuperAdmin Authority",
            "Current Governance State",
            "Current Token Version",
            "Identity Inventory Findings",
            "Runtime Hardening Performed",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 11.1 Super Admin Identity", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 11.1 Super Admin Identity / Session Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Super-admin identity and session authority", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 11.1 Super Admin Identity / Session Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("PASS 11.1 Super Admin Identity / Session Freeze", primitives, StringComparison.Ordinal);
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

    private static string ContextPath(string fileName) =>
        Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), ".ai-context", fileName);

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
