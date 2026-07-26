namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRecoverySecurityCertificationTests
{
    [Fact]
    public void RecoveryFlows_MustNotCreateAuthenticatedSessionsOrReturnBearerAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        var forgotPassword = Slice(controller, "[HttpPost(\"forgot-password\")]", "[HttpPost(\"reset-password\")]");
        var resetPassword = Slice(controller, "[HttpPost(\"reset-password\")]", "[HttpPost(\"verify-email\")]");
        var verifyEmail = Slice(controller, "[HttpPost(\"verify-email\")]", "[HttpPost(\"resend-verification\")]");
        var register = Slice(controller, "[HttpPost(\"register\")]", "[HttpPost(\"login\")]");

        Assert.DoesNotContain("SetCustomerAccessCookie", forgotPassword, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", resetPassword, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", verifyEmail, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCustomerAccessCookie", register, StringComparison.Ordinal);

        Assert.Contains("Response.Cookies.Delete", resetPassword, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", resetPassword, StringComparison.Ordinal);
        Assert.Contains("return Ok(new { message = result.Message });", resetPassword, StringComparison.Ordinal);
        Assert.Contains("return Ok(new { message = result.Message });", verifyEmail, StringComparison.Ordinal);
        Assert.Contains("return Ok(new { message = result.Message });", register, StringComparison.Ordinal);

        Assert.DoesNotContain("BuildAuthResponse", Slice(service, "public async Task<CommerceHubCustomerAuthResult> ForgotPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", Slice(service, "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync", "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync"), StringComparison.Ordinal);

        var browserResponse = Slice(controller, "private static CustomerAuthResponseDto CreateBrowserAuthResponse", "private string GetTraceId()");
        Assert.Contains("AccessToken = string.Empty", browserResponse, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordReset_MustRevokeSessionsInvalidateOlderTokensAndRecordEvidence()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var tokenEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerPasswordResetToken.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerPasswordResetTokenConfiguration.cs");

        AssertContains(tokenEntity,
            "SHA256.HashData",
            "RAW TOKEN IS NEVER STORED",
            "public const int TokenExpirationMinutes = 30",
            "RandomNumberGenerator.GetBytes(32)",
            "IsUsed",
            "UsedAtUtc");
        AssertContains(config,
            "HasMaxLength(128)",
            "IsUnique()",
            "IX_CHCustomerPasswordResetTokens_TokenHash");

        var forgotPassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ForgotPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync");
        AssertContains(forgotPassword,
            "CommerceHubCustomerPasswordResetToken.GenerateRawToken()",
            "CommerceHubCustomerPasswordResetToken.HashToken(rawToken)",
            "Where(t => t.CustomerId == customer.Id && !t.IsUsed && t.ExpiresAtUtc > now)",
            "SetProperty(t => t.IsUsed, true)",
            "PasswordResetRequested",
            "TrySendPasswordResetEmailAsync(customer, rawToken)");

        var resetPassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync");
        AssertContains(resetPassword,
            "CommerceHubCustomerPasswordResetToken.HashToken(token)",
            "resetToken.IsUsed",
            "resetToken.ExpiresAtUtc <= DateTime.UtcNow",
            "Where(t => t.Id == resetToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)",
            "ExecuteUpdateAsync",
            "consumed != 1",
            "customer.RevokeAllSessions(now)",
            "PasswordResetCompleted",
            "Please sign in with your new password");
        Assert.DoesNotContain("BuildAuthResponse", resetPassword, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailChangeRecovery_MustRequireCurrentPasswordReverifyAndKillStaleAuthority()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");

        var changeEmail = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync");
        AssertContains(changeEmail,
            "VerifyHashedPassword",
            "customer.SetEmail(normalizedEmail)",
            "customer.MarkEmailUnverified()",
            "customer.RevokeAllSessions(now)",
            "InvalidateEmailVerificationTokensAsync(customer.Id, now",
            "EmailChanged",
            "EmailVerificationRequested");

        Assert.Contains(
            "CommerceHubCustomerEmailVerificationToken.HashToken(rawToken)",
            Slice(service, "private static IssuedEmailVerificationToken CreateEmailVerificationToken", "private async Task TrySendPasswordResetEmailAsync"),
            StringComparison.Ordinal);

        var changeEmailAction = Slice(controller, "[HttpPost(\"change-email\")]", "private Guid? ResolveCustomerId()");
        AssertContains(changeEmailAction,
            "[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]",
            "Response.Cookies.Delete",
            "CreateExpiredOptions(\"/api/customers\")",
            "ClearCommerceHubRecoveryCookie()");
        Assert.DoesNotContain("SetCustomerAccessCookie", changeEmailAction, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static void AssertContains(string source, params string[] markers)
    {
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
