namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPrivilegedSessionCertificationTests
{
    [Fact]
    public void PosAdminSessions_MustBeSingleCurrentSqlSessionPerUser()
    {
        var authController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var posAuthority = ReadRepoFile("PointOfSale.API/Security/PosSessionAuthority.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        var generateTokens = Slice(authController, "private (string accessToken, string refreshToken) GenerateTokens", "// =========================================================================");
        var logout = Slice(authController, "[HttpPost(\"logout\")]", "[HttpGet(\"session\")]");
        var refresh = Slice(authController, "public async Task<ActionResult<AuthResponse>> RefreshToken", "private (string accessToken, string refreshToken) GenerateTokens");

        Assert.Contains("user.RefreshToken = sessionBinding", generateTokens, StringComparison.Ordinal);
        Assert.Contains("new(PosSecurityConstants.SessionBindingClaim, sessionBinding)", generateTokens, StringComparison.Ordinal);
        Assert.Contains("RefreshTokenHasher.Hash(presentedRefreshToken)", refresh, StringComparison.Ordinal);
        Assert.Contains("GenerateTokens(user)", refresh, StringComparison.Ordinal);
        Assert.Contains("RefreshTokenGenerator.Generate()", generateTokens, StringComparison.Ordinal);
        Assert.Contains("u.RefreshToken == sessionBinding", logout, StringComparison.Ordinal);
        Assert.Contains("user.RefreshToken = null", logout, StringComparison.Ordinal);
        Assert.Contains("MatchesCurrentSession(sessionBinding, actor.RefreshToken)", posAuthority, StringComparison.Ordinal);
        Assert.Contains("revoked_or_rotated_pos_session", posAuthority, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessTokenAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminSessions_MustBeSingleCurrentTokenVersionSessionPerPlatformActor()
    {
        var entity = ReadRepoFile("PointOfSale.Core/SuperAdmin/Models/SuperAdminUser.cs");
        var authService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminAuthService.cs");
        var platformAuthority = ReadRepoFile("PointOfSale.API/SuperAdmin/Infrastructure/SuperAdminPlatformAuthority.cs");
        var governance = ReadRepoFile("PointOfSale.API/Services/Governance/UserGovernanceService.cs");

        Assert.Contains("public int TokenVersion { get; private set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public void SetRefreshToken", entity, StringComparison.Ordinal);
        Assert.Contains("TokenVersion++", Slice(entity, "public void SetRefreshToken", "public void ClearRefreshToken"), StringComparison.Ordinal);
        Assert.Contains("TokenVersion++", Slice(entity, "public void ClearRefreshToken", "\n}"), StringComparison.Ordinal);

        Assert.Contains("superAdmin.SetRefreshToken(HashRefreshToken(refreshToken)", authService, StringComparison.Ordinal);
        Assert.Contains("SuperAdminSecurityConstants.TokenVersionClaim", authService, StringComparison.Ordinal);
        Assert.Contains("TOKEN_REUSE_DETECTED", authService, StringComparison.Ordinal);
        Assert.Contains("superAdmin.ClearRefreshToken()", authService, StringComparison.Ordinal);

        Assert.Contains("authority.TokenVersion != tokenVersion", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("STALE_SUPER_ADMIN_TOKEN", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_GOVERNANCE_REJECTED", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("platformActor?.ClearRefreshToken()", governance, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivilegedSessionCertification_MustNotClaimDeviceInventoryForPosOrSuperAdmin()
    {
        var authController = ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");
        var superAdminController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminAuthController.cs");

        Assert.DoesNotContain("active-sessions", authController, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessions/{sessionId", authController, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("active-sessions", superAdminController, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessions/{sessionId", superAdminController, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
