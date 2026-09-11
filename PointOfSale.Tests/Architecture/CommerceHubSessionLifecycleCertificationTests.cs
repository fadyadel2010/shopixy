namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSessionLifecycleCertificationTests
{
    [Fact]
    public void CommerceHubCustomerSessionLifecycle_MustRemainLoginCreatedCookieBackedAndExpiring()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var settings = ReadRepoFile("PointOfSale.API/Models/CustomerJwtSettings.cs");
        var cookies = ReadRepoFile("PointOfSale.API/Security/AuthCookieConfiguration.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        var loginMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> LoginAsync", "public async Task<CustomerProfileDto?> GetProfileAsync");
        var authResponse = Slice(service, "private CustomerAuthResponseDto BuildAuthResponse", "private string GenerateToken");
        var generateToken = Slice(service, "private string GenerateToken", "private static CustomerProfileDto MapProfile");
        var loginAction = Slice(controller, "[HttpPost(\"login\")]", "[HttpGet(\"me\")]");
        var cookieOptions = Slice(cookies, "public static CookieOptions CreateHttpOnlyOptions", "public static CookieOptions CreateExpiredOptions");
        var customerJwtScheme = Slice(program, ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme", "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");

        Assert.Contains("if (!customer.IsEmailVerified)", loginMethod, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customer.Id", loginMethod, StringComparison.Ordinal);
        Assert.Contains("Response = BuildAuthResponse(customer)", loginMethod, StringComparison.Ordinal);
        Assert.Contains("AccessToken = GenerateToken(customer, issuedAtUtc, expiresAt)", authResponse, StringComparison.Ordinal);
        Assert.Contains("issuedAtUtc.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes)", authResponse, StringComparison.Ordinal);
        Assert.Contains("JwtRegisteredClaimNames.Jti", generateToken, StringComparison.Ordinal);
        Assert.Contains("CustomerSecurityConstants.SessionIssuedAtTicksClaim", generateToken, StringComparison.Ordinal);
        Assert.Contains("SetCustomerAccessCookie(result.Response!)", loginAction, StringComparison.Ordinal);
        Assert.Contains("AccessToken = string.Empty", controller, StringComparison.Ordinal);
        Assert.Contains("public int AccessTokenExpirationMinutes { get; set; } = 60;", settings, StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", cookieOptions, StringComparison.Ordinal);
        Assert.Contains("Secure = true", cookieOptions, StringComparison.Ordinal);
        Assert.Contains("SameSite = SameSiteMode.Strict", cookieOptions, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime = true", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("ClockSkew = TimeSpan.Zero", customerJwtScheme, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubCustomerSessionLifecycle_MustDestroyBrowserAndRecoveryAuthorityOnTerminalEvents()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        var logoutAction = Slice(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"revoke-all-sessions\")]");
        var revokeAllAction = Slice(controller, "[HttpPost(\"revoke-all-sessions\")]", "[HttpPost(\"forgot-password\")]");
        var resetPasswordAction = Slice(controller, "[HttpPost(\"reset-password\")]", "[HttpPost(\"verify-email\")]");
        var changePasswordAction = Slice(controller, "[HttpPost(\"change-password\")]", "[HttpPost(\"change-email\")]");
        var changeEmailAction = Slice(controller, "[HttpPost(\"change-email\")]", "private Guid? ResolveCustomerId()");
        var resetPasswordMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync");
        var changePasswordMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangePasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync");
        var changeEmailMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync");

        Assert.Contains("RevokeTokenAsync(accessToken, \"logout\"", logoutAction, StringComparison.Ordinal);
        Assert.Contains("ClearCustomerAccessCookies()", logoutAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", logoutAction, StringComparison.Ordinal);
        Assert.Contains("LoggedOut", logoutAction, StringComparison.Ordinal);

        Assert.Contains("RevokeAllTokensAsync", revokeAllAction, StringComparison.Ordinal);
        Assert.Contains("ClearCustomerAccessCookies()", revokeAllAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", revokeAllAction, StringComparison.Ordinal);
        Assert.Contains("AllSessionsRevoked", revokeAllAction, StringComparison.Ordinal);

        Assert.Contains("customer.RevokeAllSessions(cutoffUtc)", revocationService, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", resetPasswordMethod, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(DateTime.UtcNow)", changePasswordMethod, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", changeEmailMethod, StringComparison.Ordinal);
        Assert.Contains("ClearCustomerAccessCookies()", resetPasswordAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", resetPasswordAction, StringComparison.Ordinal);
        Assert.Contains("ClearCustomerAccessCookies()", changePasswordAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", changePasswordAction, StringComparison.Ordinal);
        Assert.Contains("ClearCustomerAccessCookies()", changeEmailAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", changeEmailAction, StringComparison.Ordinal);
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
