namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSessionCreationCertificationTests
{
    [Fact]
    public void CustomerSessions_MustOnlyBeCreatedByLogin()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");

        Assert.Contains("AccessToken = GenerateToken(customer, issuedAtUtc, expiresAt)", service, StringComparison.Ordinal);
        Assert.Contains("Response = BuildAuthResponse(customer)", LoginMethod(service), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", RegistrationMethod(service), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", VerifyEmailMethod(service), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", ResetPasswordMethod(service), StringComparison.Ordinal);

        Assert.Contains("SetCustomerAccessCookie(result.Response!)", LoginAction(controller), StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", RegistrationAction(controller), StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", VerifyEmailAction(controller), StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", ResetPasswordAction(controller), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSessions_MustBeDestroyedByLogoutAndDurableCutoff()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var revocationService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        var logoutAction = Slice(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"revoke-all-sessions\")]");
        Assert.Contains("RevokeTokenAsync(accessToken, \"logout\"", logoutAction, StringComparison.Ordinal);
        Assert.Contains("Response.Cookies.Delete", logoutAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", logoutAction, StringComparison.Ordinal);
        Assert.Contains("LoggedOut", logoutAction, StringComparison.Ordinal);

        Assert.Contains("customer.RevokeAllSessions(cutoffUtc)", revocationService, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", revocationService, StringComparison.Ordinal);
        Assert.Contains("tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value", revocationService, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", ResetPasswordMethod(service), StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(DateTime.UtcNow)", ChangePasswordMethod(service), StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", ChangeEmailMethod(service), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerJwtValidation_MustEnforceJtiRevocationCutoffAndGovernance()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var customerJwtScheme = Slice(program, ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme", "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");

        Assert.Contains("CommerceHubCustomerAccessTokenCookieName", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("JwtRegisteredClaimNames.Jti", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("IsTokenRevokedAsync(jtiClaim", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("customerId", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customerId", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("MapInboundClaims = false", customerJwtScheme, StringComparison.Ordinal);
    }

    private static string RegistrationMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> RegisterAsync", "public async Task<CommerceHubCustomerAuthResult> LoginAsync");

    private static string LoginMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> LoginAsync", "public async Task<CustomerProfileDto?> GetProfileAsync");

    private static string ResetPasswordMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync");

    private static string VerifyEmailMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync", "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync");

    private static string ChangePasswordMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> ChangePasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync");

    private static string ChangeEmailMethod(string source) => Slice(source, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync");

    private static string RegistrationAction(string source) => Slice(source, "[HttpPost(\"register\")]", "[HttpPost(\"login\")]");

    private static string LoginAction(string source) => Slice(source, "[HttpPost(\"login\")]", "[HttpGet(\"me\")]");

    private static string ResetPasswordAction(string source) => Slice(source, "[HttpPost(\"reset-password\")]", "[HttpPost(\"verify-email\")]");

    private static string VerifyEmailAction(string source) => Slice(source, "[HttpPost(\"verify-email\")]", "[HttpPost(\"resend-verification\")]");

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
