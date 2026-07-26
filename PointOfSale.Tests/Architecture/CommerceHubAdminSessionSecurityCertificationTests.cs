namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubAdminSessionSecurityCertificationTests
{
    [Fact]
    public void PosAdminTokenValidation_MustRevalidateRoleTenantUserAndSessionBinding()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var authority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var tests = ReadRepoFile("PointOfSale.Tests/Security/PosSessionAuthorityTests.cs");

        AssertContains(program,
            "AddScoped<IPosSessionAuthority, PosSessionAuthority>()",
            "ValidateAccessTokenAsync",
            "POS session is no longer current");

        AssertContains(authority,
            "TryGetGuidClaim(principal, ClaimTypes.NameIdentifier",
            "TryGetGuidClaim(principal, \"tenant_id\"",
            "actor.IsDeleted || !actor.IsActive",
            "actor.TenantId != tokenTenantId",
            "!string.Equals(actor.Role.ToString(), tokenRole",
            "MatchesCurrentSession(sessionBinding, actor.RefreshToken)",
            "TenantLineageAuthority.IsActiveTenantAsync",
            "tenant_governance_banned");

        AssertContains(tests,
            "ValidateAccessTokenAsync_RejectsStaleRoleAndInactiveActorAuthority",
            "ValidateAccessTokenAsync_RejectsInactiveOrGovernanceBannedTenant",
            "ValidateAccessTokenAsync_RejectsLogoutOrRefreshRotatedSession",
            "ValidateAccessTokenAsync_EmitsBoundedRevokedSessionDiagnostic");
    }

    [Fact]
    public void PosAdminRefreshLogoutAndOnboarding_MustNotRestoreStalePrivileges()
    {
        var auth = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var onboardingService = ReadRepoFile("PointOfSale.API/Services/Platform/TenantRegistrationService.cs");
        var platformController = ReadRepoFile("PointOfSale.API/Controllers/PlatformController.cs");
        var concurrencyTests = ReadRepoFile("PointOfSale.Tests/Security/PosSessionConcurrencyTests.cs");

        AssertContains(auth,
            "RefreshTokenHasher.Hash(presentedRefreshToken)",
            "GenerateTokens(user)",
            "RefreshTokenGenerator.Generate()",
            "user.RefreshToken = sessionBinding",
            "new(PosSecurityConstants.SessionBindingClaim, sessionBinding)",
            "user.RefreshToken = null",
            "SESSION_REVOCATION_FAILED",
            "REFRESH_REPLAY_REJECTED");

        foreach (var source in new[] { onboardingService, platformController })
        {
            AssertContains(source,
                "RefreshTokenGenerator.Generate()",
                "RefreshTokenHasher.Hash(refreshToken)",
                "PosSecurityConstants.SessionBindingClaim");
        }

        AssertContains(platformController,
            "EnsureTenantLoginAllowedAsync",
            "TenantLineageAuthority.IsActiveTenantAsync",
            "SESSION_RESTORATION_FORBIDDEN");

        Assert.Contains("ConcurrentSessionRotations_RejectStaleWriterInsteadOfResurrectingSession", concurrencyTests, StringComparison.Ordinal);
    }

    [Fact]
    public void PosAdminPermissionAuthority_MustRemainCurrentRoleBasedUntilDedicatedPermissionsExist()
    {
        var authority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var findings = ReadRepoFile("docs/architecture/commercehub-privileged-authority-certification-findings.md");

        Assert.Contains("stale_actor_role_claim", authority, StringComparison.Ordinal);
        Assert.Contains("No separate POS permission entity was found", findings, StringComparison.Ordinal);
        Assert.Contains("role-change revalidation", findings, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static void AssertContains(string source, params string[] markers)
    {
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }
}
