namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSessionIsolationCertificationTests
{
    [Fact]
    public void CustomerLogout_MustRevokeOnlyCurrentJtiDuringNormalLogout()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        var logoutAction = Slice(controller, "[HttpPost(\"logout\")]", "[HttpPost(\"revoke-all-sessions\")]");
        var revokeToken = Slice(revocationService, "public async Task RevokeTokenAsync", "public async Task<bool> IsTokenRevokedAsync");

        Assert.Contains("Request.Cookies.TryGetValue", logoutAction, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCustomerAccessTokenCookieName", logoutAction, StringComparison.Ordinal);
        Assert.Contains("RevokeTokenAsync(accessToken, \"logout\"", logoutAction, StringComparison.Ordinal);
        Assert.Contains("Response.Cookies.Delete", logoutAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", logoutAction, StringComparison.Ordinal);

        Assert.Contains("JwtRegisteredClaimNames.Jti", revokeToken, StringComparison.Ordinal);
        Assert.Contains("RevokedCustomerAccessTokens.Add", revokeToken, StringComparison.Ordinal);
        Assert.DoesNotContain("RevokeAllTokensAsync", revokeToken, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSecurityEvents_MustRevokeEveryExistingSessionThroughCutoff()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var revocationService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerTokenRevocationService.cs");

        var revokeAllAction = Slice(controller, "[HttpPost(\"revoke-all-sessions\")]", "[HttpPost(\"forgot-password\")]");
        var revokeAllMethod = Slice(revocationService, "public async Task<DateTime> RevokeAllTokensAsync", "public async Task CleanupExpiredTokensAsync");
        var cutoffValidation = Slice(revocationService, "public async Task EnsureTokenSessionStillValidAsync", "public async Task<DateTime> RevokeAllTokensAsync");
        var resetPassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync");
        var changePassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangePasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync");
        var changeEmail = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync");

        Assert.Contains("RevokeAllTokensAsync", revokeAllAction, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(cutoffUtc)", revokeAllMethod, StringComparison.Ordinal);
        Assert.Contains("TokensValidAfterUtc", cutoffValidation, StringComparison.Ordinal);
        Assert.Contains("tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value", cutoffValidation, StringComparison.Ordinal);
        Assert.Contains("CustomerTokenSessionRevokedException", cutoffValidation, StringComparison.Ordinal);

        Assert.Contains("customer.RevokeAllSessions(now)", resetPassword, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(DateTime.UtcNow)", changePassword, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", changeEmail, StringComparison.Ordinal);
        Assert.Contains("MarkEmailUnverified()", changeEmail, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProtectedRequests_MustFailClosedWhenRevocationOrGovernanceCannotValidate()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var customerJwtScheme = Slice(
            program,
            ".AddJwtBearer(PointOfSale.API.Security.CustomerSecurityConstants.AuthenticationScheme",
            "// Use [Authorize(AuthenticationSchemes = \"AffiliateJwt\")]");

        Assert.Contains("IsTokenRevokedAsync(jtiClaim", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureTokenSessionStillValidAsync", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync(customerId", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(\"Token revocation status could not be verified.\")", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("catch (PointOfSale.API.Security.CustomerTokenSessionRevokedException ex)", customerJwtScheme, StringComparison.Ordinal);
        Assert.Contains("context.Fail(ex.Message)", customerJwtScheme, StringComparison.Ordinal);
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
