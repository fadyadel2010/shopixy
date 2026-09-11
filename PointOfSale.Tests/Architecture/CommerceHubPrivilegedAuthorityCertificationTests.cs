namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPrivilegedAuthorityCertificationTests
{
    [Fact]
    public void PrivilegedAuthorityInventory_MustRemainSqlCurrentStateAuthoritative()
    {
        var posAuthority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var superAdminAuthority = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs");
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var emergencyGate = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs");

        AssertContains(posAuthority,
            "_dbContext.Users",
            "user.Role",
            "user.IsActive",
            "user.IsDeleted",
            "MatchesCurrentSession(sessionBinding, actor.RefreshToken)",
            "TenantLineageAuthority.IsActiveTenantAsync",
            "UserGovernanceRole.Vendor",
            "tenant_governance_banned",
            "OperationalConvergenceAuthority.PosSession");

        AssertContains(superAdminAuthority,
            "_dbContext.SuperAdmins",
            "authority.TokenVersion != tokenVersion",
            "_dbContext.UserGovernanceProfiles",
            "UserGovernanceRole.Admin",
            "SUPER_ADMIN_GOVERNANCE_REJECTED",
            "OperationalConvergenceAuthority.PlatformAdmin");

        AssertContains(governance,
            "subject.Role == UserGovernanceRole.Admin",
            "platformActor?.ClearRefreshToken()",
            "UserGovernanceAuditLog");

        AssertContains(emergencyGate,
            "CommerceHubEmergencyStopOptions",
            "PaymentIntakeStopped",
            "OutwardMoneyMovementStopped",
            "Blocking CommerceHub");
    }

    [Fact]
    public void AuthorityChangingEvents_MustBeFrozenAgainstStalePrivilegedSurvival()
    {
        var posAuthority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var authController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var superAdminEntity = ReadRepoFile("PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs");
        var superAdminManagement = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminManagementService.cs");
        var superAdminAuth = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminAuthService.cs");

        AssertContains(posAuthority,
            "stale_actor_role_claim",
            "inactive_or_deleted_actor",
            "tenant_inactive_or_deleted",
            "revoked_or_rotated_pos_session");

        AssertContains(authController,
            "EnsureTenantLoginAllowedAsync(user.TenantId",
            "RefreshTokenHasher.Hash(presentedRefreshToken)",
            "GenerateTokens(user)",
            "user.RefreshToken = null",
            "SESSION_REVOCATION_FAILED");

        AssertContains(superAdminEntity,
            "public int TokenVersion { get; private set; }",
            "SetRefreshToken",
            "ClearRefreshToken",
            "TokenVersion++");

        AssertContains(superAdminManagement,
            "superAdmin.UpdatePasswordHash",
            "superAdmin.ClearRefreshToken()");

        AssertContains(superAdminAuth,
            "EnsureAdminLoginAllowedAsync(superAdmin.Id",
            "superAdmin.ClearRefreshToken()",
            "TOKEN_REUSE_DETECTED",
            "SuperAdminSecurityConstants.TokenVersionClaim");
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPrivilegedAuthorityVerdict()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-privileged-authority-certification-findings.md");

        AssertContains(findings,
            "PASS 16.1B-D Privileged Session Security Certification",
            "Privileged Authority Inventory",
            "Authority Matrix",
            "Required Answers",
            "Remaining Debt");
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
