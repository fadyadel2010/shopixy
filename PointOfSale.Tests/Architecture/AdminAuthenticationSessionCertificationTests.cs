namespace PointOfSale.Tests.Architecture;

public sealed class AdminAuthenticationSessionCertificationTests
{
    [Fact]
    public void PosAccessTokens_MustRecomposeCurrentSqlSessionAuthority()
    {
        AssertContains(
            ApiPath("Security", "PosSessionAuthority.cs"),
            "current SQL session and actor scope",
            "PosSecurityConstants.SessionBindingClaim",
            "_dbContext.Users",
            "TenantLineageAuthority.IsActiveTenantAsync",
            "UserGovernanceRole.Vendor",
            "OperationalConvergenceAuthority.PosSession");

        AssertContains(
            ApiPath("Program.cs"),
            "AddScoped<IPosSessionAuthority, PosSessionAuthority>()",
            "ValidateAccessTokenAsync",
            "POS session is no longer current");
    }

    [Fact]
    public void PosRefreshAndLogout_MustRotateAndRevokeCurrentSqlSession()
    {
        var authSource = File.ReadAllText(ApiPath("Controllers", "AuthController.cs"));
        AssertContains(
            ApiPath("Controllers", "AuthController.cs"),
            "RefreshTokenHasher.Hash(presentedRefreshToken)",
            "RefreshTokenGenerator.Generate()",
            "PosSecurityConstants.SessionBindingClaim",
            "REFRESH_REPLAY_REJECTED",
            "session already rotated or revoked",
            "SESSION_REVOCATION_FAILED",
            "Retry logout.");
        Assert.DoesNotContain(
            "u.RefreshToken == presentedRefreshToken)",
            authSource,
            StringComparison.Ordinal);

        AssertContains(
            InfrastructurePath("Data", "Configurations", "UserConfiguration.cs"),
            "IsConcurrencyToken()");
    }

    [Fact]
    public void PosStaffRegistration_MustFailClosedUntilInvitationAcceptanceExists()
    {
        var authSource = File.ReadAllText(ApiPath("Controllers", "AuthController.cs"));
        var register = ExtractMethod(authSource, "Register(");

        Assert.Contains("POS_STAFF_REGISTRATION_DISABLED", register, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status410Gone", register, StringComparison.Ordinal);
        Assert.Contains("PosPermissions.UsersInvite", authSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", register, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Users.Add", register, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", register, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateTokens(", register, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSessionCookies", register, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshTokenGenerator.Generate()", register, StringComparison.Ordinal);
    }

    [Fact]
    public void PosTenantOnboardingSessionIssuers_MustUseCurrentSqlSessionBinding()
    {
        var registrationService = File.ReadAllText(ApiPath("Services", "Platform", "TenantRegistrationService.cs"));
        var platformController = File.ReadAllText(ApiPath("Controllers", "PlatformController.cs"));

        foreach (var source in new[] { registrationService, platformController })
        {
            Assert.Contains("RefreshTokenGenerator.Generate()", source, StringComparison.Ordinal);
            Assert.Contains("RefreshTokenHasher.Hash(refreshToken)", source, StringComparison.Ordinal);
            Assert.Contains("PosSecurityConstants.SessionBindingClaim", source, StringComparison.Ordinal);
            Assert.DoesNotContain("user.RefreshToken = refreshToken", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RandomNumberGenerator.GetBytes", source, StringComparison.Ordinal);
        }

        Assert.Contains("EnsureTenantLoginAllowedAsync", platformController, StringComparison.Ordinal);
        Assert.Contains("TenantLineageAuthority.IsActiveTenantAsync", platformController, StringComparison.Ordinal);
        Assert.Contains("SESSION_RESTORATION_FORBIDDEN", platformController, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminRefreshAndGovernanceActions_MustFailClosedForRestrictedPlatformIdentity()
    {
        AssertContains(
            ApiPath("SuperAdmin", "Services", "SuperAdminAuthService.cs"),
            "EnsureAdminLoginAllowedAsync",
            "SuperAdmin refresh rejected by current governance state; refresh authority revoked",
            "superAdmin.ClearRefreshToken()");

        AssertContains(
            ApiPath("Services", "Governance", "UserGovernanceService.cs"),
            "SUPER_ADMIN_SUSPENDED",
            "SUPER_ADMIN_FINANCIAL_LOCKED",
            "platformActor?.ClearRefreshToken()");
    }

    [Fact]
    public void CertificationDocumentation_MustFreezeIdentityAndRemainingDebt()
    {
        AssertContains(
            DocsPath("architecture", "admin-authentication-session-certification-findings.md"),
            "POS-ADMIN-CERTIFICATION-01",
            "Authentication Lifecycle Findings",
            "Session Replay And Revocation Findings",
            "Operational Identity Auditability",
            "Regression Freeze Invariants",
            "Remaining Identity-Risk Debt");
    }

    private static void AssertContains(string path, params string[] markers)
    {
        var text = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, text, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([Root(), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([Root(), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([Root(), "docs", .. parts]);

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Solution root was not found.");
    }

    private static string ExtractMethod(string source, string methodMarker)
    {
        var markerIndex = source.IndexOf(methodMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method marker {methodMarker}.");
        }

        var braceIndex = source.IndexOf('{', markerIndex);
        if (braceIndex < 0)
        {
            throw new InvalidOperationException($"Could not find method body for {methodMarker}.");
        }

        var depth = 0;
        for (var index = braceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[braceIndex..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for {methodMarker}.");
    }
}
