namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubConcurrentSessionCertificationTests
{
    [Fact]
    public void CustomerLogin_MustAllowIndependentConcurrentAccessJwtSessions()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var frontendSettings = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerSettingsPage.tsx");
        var authService = ReadRepoFile("PointOfSale.CommerceHub/src/services/customer-auth-service.ts");

        var loginMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> LoginAsync", "public async Task<CustomerProfileDto?> GetProfileAsync");
        var buildAuthResponse = Slice(service, "private CustomerAuthResponseDto BuildAuthResponse", "private string GenerateToken");
        var generateToken = Slice(service, "private string GenerateToken", "private static CustomerProfileDto MapProfile");

        Assert.Contains("Response = BuildAuthResponse(customer)", loginMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeAllSessions", loginMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeAllTokensAsync", loginMethod, StringComparison.Ordinal);
        Assert.Contains("AccessToken = GenerateToken(customer, issuedAtUtc, expiresAt)", buildAuthResponse, StringComparison.Ordinal);
        Assert.Contains("new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())", generateToken, StringComparison.Ordinal);
        Assert.Contains("CustomerSecurityConstants.SessionIssuedAtTicksClaim", generateToken, StringComparison.Ordinal);

        Assert.Contains("commercehub_customer_access_token", ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs"), StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"revoke-all-sessions\")]", controller, StringComparison.Ordinal);
        Assert.Contains("revokeAllSessions", authService, StringComparison.Ordinal);
        Assert.Contains("Revoke all sessions", frontendSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("/sessions", authService, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSessionInventory_MustRemainAccessTokenOnlyWithoutRefreshOrDeviceStore()
    {
        var authService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.DoesNotContain("RefreshToken", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost(\"refresh", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomerRefreshToken", dbContext, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerActiveSession", dbContext, StringComparison.Ordinal);

        Assert.Contains("RevokedCustomerAccessTokens", revocationService, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", revocationService, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentSessionCertificationImplementation_MustRecordMatrixAndDebt()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-concurrent-session-certification-findings.md");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        foreach (var marker in new[]
        {
            "PASS 16.1B-C",
            "Concurrent-Session Inventory",
            "Authority Matrix",
            "Required Answers",
            "Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())", service, StringComparison.Ordinal);
        Assert.Contains("CustomerSecurityConstants.SessionIssuedAtTicksClaim", service, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"revoke-all-sessions\")]", controller, StringComparison.Ordinal);
        Assert.Contains("RevokeAllTokensAsync", controller, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", revocationService, StringComparison.Ordinal);
        Assert.Contains("RevokedCustomerAccessTokens", revocationService, StringComparison.Ordinal);
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
