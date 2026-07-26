namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSuperAdminSessionCertificationTests
{
    [Fact]
    public void SuperAdminAccessTokens_MustBeSubordinateToCurrentTokenVersionAndGovernance()
    {
        var authority = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs");
        var middleware = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthorityMiddleware.cs");
        var tests = ReadRepoFile("PointOfSale.Tests/SuperAdmin/SuperAdminPlatformAuthorityTests.cs");

        AssertContains(authority,
            "SuperAdminTokenClaimsValidator.HasRequiredClaims",
            "_dbContext.SuperAdmins",
            "authority.TokenVersion != tokenVersion",
            "STALE_SUPER_ADMIN_TOKEN",
            "_dbContext.UserGovernanceProfiles",
            "SUPER_ADMIN_GOVERNANCE_REJECTED",
            "OperationalConvergenceTelemetry.RecordAuthorityFailure");

        AssertContains(middleware,
            "GetMetadata<IAllowAnonymous>()",
            "ValidatePlatformActorAsync",
            "SUPER_ADMIN_SCOPE_FORBIDDEN");

        AssertContains(tests,
            "ValidatePlatformActorAsync_Authorizes_CurrentSqlSuperAdmin_WithMatchingTokenVersion",
            "ValidatePlatformActorAsync_Rejects_StaleAccessTokenVersion",
            "ValidatePlatformActorAsync_Rejects_GovernanceRestrictedSuperAdmin",
            "ValidatePlatformActorAsync_Rejects_DeletedSuperAdmin");
    }

    [Fact]
    public void SuperAdminRefreshLogoutPasswordAndGovernanceChanges_MustAdvanceSessionAuthority()
    {
        var entity = ReadRepoFile("PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs");
        var auth = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminAuthService.cs");
        var management = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminManagementService.cs");
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");
        var tests = ReadRepoFile("PointOfSale.Tests/SuperAdmin/SuperAdminAuthServiceTests.cs");

        AssertContains(entity,
            "SetRefreshToken",
            "ClearRefreshToken",
            "TokenVersion++");

        AssertContains(auth,
            "superAdmin.SetRefreshToken(HashRefreshToken(refreshToken)",
            "SuperAdminSecurityConstants.TokenVersionClaim",
            "EnsureAdminLoginAllowedAsync(superAdmin.Id",
            "superAdmin.ClearRefreshToken()",
            "TOKEN_REUSE_DETECTED",
            "TOKEN_REFRESH_FAILED");

        AssertContains(management,
            "superAdmin.UpdatePasswordHash",
            "superAdmin.ClearRefreshToken()");

        AssertContains(governance,
            "subject.Role == UserGovernanceRole.Admin",
            "targetStatus != UserGovernanceStatus.Active",
            "platformActor?.ClearRefreshToken()");

        AssertContains(tests,
            "UpdatePasswordAsync_Revokes_Existing_Sessions_For_Mutable_SuperAdmin",
            "LoginAsync_Rejects_Suspended_SuperAdmin_Governance",
            "RefreshAsync_Rejects_GovernanceRestricted_SuperAdmin_And_Revokes_CurrentRefreshAuthority");
    }

    [Fact]
    public void SuperAdminOperationalAudit_MustRunAfterCurrentPlatformAuthority()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var audit = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminOperationalAuditMiddleware.cs");

        Assert.Contains("UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminPlatformAuthorityMiddleware>()", program, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<PointOfSale.API.SuperAdmin.Infrastructure.SuperAdminOperationalAuditMiddleware>()", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("SuperAdminPlatformAuthorityMiddleware", StringComparison.Ordinal) <
            program.IndexOf("SuperAdminOperationalAuditMiddleware", StringComparison.Ordinal));

        AssertContains(audit,
            "GetMetadata<IAllowAnonymous>()",
            "PLATFORM_OPERATION_COMPLETED",
            "SuperAdminPlatformAuthority.SuperAdminId");
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
