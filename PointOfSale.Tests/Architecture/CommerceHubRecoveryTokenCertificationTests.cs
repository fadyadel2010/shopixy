namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRecoveryTokenCertificationTests
{
    [Fact]
    public void PasswordResetTokens_MustBeHashOnlyUniqueExpiringAndSingleUse()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerPasswordResetToken.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerPasswordResetTokenConfiguration.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        AssertContains(entity,
            "RandomNumberGenerator.GetBytes(32)",
            "Convert.ToBase64String(bytes)",
            "SHA256.HashData",
            "Convert.ToHexStringLower(hash)",
            "TokenExpirationMinutes = 30",
            "IsUsed",
            "UsedAtUtc");

        AssertContains(config,
            "TokenHash",
            "HasMaxLength(128)",
            "IsUnique()",
            "IX_CHCustomerPasswordResetTokens_TokenHash",
            "IX_CHCustomerPasswordResetTokens_CustomerId");

        var resetPassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync");
        AssertContains(resetPassword,
            "CommerceHubCustomerPasswordResetToken.HashToken(token)",
            "AsNoTracking()",
            "resetToken.IsUsed",
            "resetToken.ExpiresAtUtc <= DateTime.UtcNow",
            "Where(t => t.Id == resetToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)",
            "ExecuteUpdateAsync",
            "SetProperty(t => t.IsUsed, true)",
            "SetProperty(t => t.UsedAtUtc, now)",
            "consumed != 1",
            "TOKEN_ALREADY_USED");
    }

    [Fact]
    public void EmailVerificationTokens_MustBeHashOnlyUniqueExpiringAndSingleUse()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerEmailVerificationToken.cs");
        var config = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerEmailVerificationTokenConfiguration.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        AssertContains(entity,
            "RandomNumberGenerator.GetBytes(32)",
            "Convert.ToBase64String(bytes)",
            "SHA256.HashData",
            "Convert.ToHexStringLower(hash)",
            "TokenExpirationHours = 24",
            "IsUsed",
            "UsedAtUtc");

        AssertContains(config,
            "TokenHash",
            "HasMaxLength(128)",
            "IsUnique()",
            "IX_CHCustomerEmailVerificationTokens_TokenHash",
            "IX_CHCustomerEmailVerificationTokens_CustomerId");

        var verifyEmail = Slice(service, "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync", "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync");
        AssertContains(verifyEmail,
            "CommerceHubCustomerEmailVerificationToken.HashToken(token)",
            "AsNoTracking()",
            "verificationToken.IsUsed",
            "verificationToken.ExpiresAtUtc <= DateTime.UtcNow",
            "Where(t => t.Id == verificationToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)",
            "ExecuteUpdateAsync",
            "SetProperty(t => t.IsUsed, true)",
            "SetProperty(t => t.UsedAtUtc, now)",
            "consumed != 1",
            "TOKEN_ALREADY_USED");
        Assert.DoesNotContain("BuildAuthResponse", verifyEmail, StringComparison.Ordinal);
    }

    [Fact]
    public void NewRecoveryTokenIssuance_MustInvalidateOlderLiveTokensForSameCustomer()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        var forgotPassword = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ForgotPasswordAsync", "public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync");
        AssertContains(forgotPassword,
            "BeginTransactionAsync",
            "Where(t => t.CustomerId == customer.Id && !t.IsUsed && t.ExpiresAtUtc > now)",
            "ExecuteUpdateAsync",
            "SetProperty(t => t.IsUsed, true)",
            "_dbContext.CommerceHubCustomerPasswordResetTokens.Add",
            "transaction.CommitAsync");

        var resendVerification = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync", "public async Task<CommerceHubCustomerAuthResult> UpdateProfileAsync");
        AssertContains(resendVerification,
            "BeginTransactionAsync",
            "InvalidateEmailVerificationTokensAsync(customer.Id, now",
            "_dbContext.CommerceHubCustomerEmailVerificationTokens.Add",
            "transaction.CommitAsync");

        var invalidateVerification = Slice(service, "private async Task InvalidateEmailVerificationTokensAsync", "private static IssuedEmailVerificationToken CreateEmailVerificationToken");
        AssertContains(invalidateVerification,
            "Where(t => t.CustomerId == customerId && !t.IsUsed && t.ExpiresAtUtc > now)",
            "ExecuteUpdateAsync",
            "SetProperty(t => t.IsUsed, true)",
            "SetProperty(t => t.UsedAtUtc, now)");
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
