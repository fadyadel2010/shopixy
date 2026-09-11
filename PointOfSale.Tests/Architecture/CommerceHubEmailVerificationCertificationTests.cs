namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubEmailVerificationCertificationTests
{
    [Fact]
    public void VerificationTokens_MustRemainHashedExpiringAndSingleUse()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/CommerceHubCustomerEmailVerificationToken.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubCustomerEmailVerificationTokenConfiguration.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var appSettings = CertificationSource.ReadRepoFile("PointOfSale.API/appsettings.json");

        Assert.Contains("SHA256.HashData", entity, StringComparison.Ordinal);
        Assert.Contains("RAW TOKEN IS NEVER STORED", entity, StringComparison.Ordinal);
        Assert.Contains("public const int TokenExpirationHours = 24", entity, StringComparison.Ordinal);
        Assert.Contains("IsUsed", entity, StringComparison.Ordinal);
        Assert.Contains("UsedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("IX_CHCustomerEmailVerificationTokens_TokenHash", config, StringComparison.Ordinal);

        var verifyMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync", "public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync");
        Assert.Contains("CommerceHubCustomerEmailVerificationToken.HashToken(token)", verifyMethod, StringComparison.Ordinal);
        Assert.Contains("Where(t => t.Id == verificationToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)", verifyMethod, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync", verifyMethod, StringComparison.Ordinal);
        Assert.Contains("consumed != 1", verifyMethod, StringComparison.Ordinal);
        Assert.Contains("customer.MarkEmailVerified()", verifyMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAuthResponse", verifyMethod, StringComparison.Ordinal);
        Assert.Contains("public string PublicBaseUrl { get; set; } = \"http://localhost:5175\";", service, StringComparison.Ordinal);
        Assert.Contains("\"PublicBaseUrl\": \"http://localhost:5175\"", appSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailChange_MustResetVerificationAndRevokeActiveSessions()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CustomersAuthController.cs");

        var changeEmail = Slice(service, "public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync", "private async Task InvalidateEmailVerificationTokensAsync");
        Assert.Contains("VerifyHashedPassword", changeEmail, StringComparison.Ordinal);
        Assert.Contains("customer.SetEmail(normalizedEmail)", changeEmail, StringComparison.Ordinal);
        Assert.Contains("customer.MarkEmailUnverified()", changeEmail, StringComparison.Ordinal);
        Assert.Contains("customer.RevokeAllSessions(now)", changeEmail, StringComparison.Ordinal);
        Assert.Contains("InvalidateEmailVerificationTokensAsync(customer.Id, now", changeEmail, StringComparison.Ordinal);
        Assert.Contains("EmailChanged", changeEmail, StringComparison.Ordinal);
        Assert.Contains("EmailVerificationRequested", changeEmail, StringComparison.Ordinal);

        var changeEmailAction = Slice(controller, "[HttpPost(\"change-email\")]", "private Guid? ResolveCustomerId()");
        Assert.Contains("ClearCustomerAccessCookies()", changeEmailAction, StringComparison.Ordinal);
        Assert.Contains("ClearCommerceHubRecoveryCookie()", changeEmailAction, StringComparison.Ordinal);
    }

    [Fact]
    public void UnverifiedCustomers_MustNotAuthenticate()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");
        var loginMethod = Slice(service, "public async Task<CommerceHubCustomerAuthResult> LoginAsync", "public async Task<CustomerProfileDto?> GetProfileAsync");

        Assert.Contains("if (!customer.IsEmailVerified)", loginMethod, StringComparison.Ordinal);
        Assert.Contains("return Fail(\"EMAIL_NOT_VERIFIED\"", loginMethod, StringComparison.Ordinal);
        Assert.Contains("VerifyHashedPassword", loginMethod, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerLoginAllowedAsync", loginMethod, StringComparison.Ordinal);
        Assert.Contains("Response = BuildAuthResponse(customer)", loginMethod, StringComparison.Ordinal);
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
