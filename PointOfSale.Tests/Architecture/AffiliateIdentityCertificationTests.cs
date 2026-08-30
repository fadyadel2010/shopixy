namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateIdentityCertificationTests
{
    [Fact]
    public void RegistrationAndVerification_MustNotCreateAuthenticatedAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");

        var registration = Slice(controller, "[HttpPost(\"register\")]", "[HttpPost(\"login\")]");
        Assert.Contains("await ReplaceVerificationTokenAsync(affiliateUser, ct)", registration, StringComparison.Ordinal);
        Assert.Contains("await registrationTransaction.CommitAsync(ct)", registration, StringComparison.Ordinal);
        Assert.Contains("await SendVerificationEmailAsync(affiliateUser, rawVerificationToken)", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSessionCookies", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateTokens", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("SetRefreshToken", registration, StringComparison.Ordinal);

        var verification = Slice(controller, "[HttpPost(\"verify-email\")]", "[HttpPost(\"resend-verification\")]");
        Assert.Contains("AffiliateEmailVerificationToken.HashToken(request.Token)", verification, StringComparison.Ordinal);
        Assert.Contains("token.CanConsume(now)", verification, StringComparison.Ordinal);
        Assert.Contains("token.Consume(now)", verification, StringComparison.Ordinal);
        Assert.Contains("token.AffiliateUser.VerifyEmail()", verification, StringComparison.Ordinal);
        Assert.True(
            verification.IndexOf("var eligibleUserRows", StringComparison.Ordinal) <
            verification.IndexOf("var claimedRows", StringComparison.Ordinal),
            "Verification must lock the user before claiming the token.");
        Assert.DoesNotContain("SetSessionCookies", verification, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateTokens", verification, StringComparison.Ordinal);

        var replacement = Slice(controller, "private async Task<string?> ReplaceVerificationTokenAsync", "private async Task SendVerificationEmailAsync");
        Assert.True(
            replacement.IndexOf("var eligibleRows", StringComparison.Ordinal) <
            replacement.IndexOf("ExecuteDeleteAsync", StringComparison.Ordinal),
            "Resend must lock the user before replacing outstanding tokens.");
    }

    [Fact]
    public void VerificationTokens_MustBeHashOnlyExpiringSingleUseSqlAuthority()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateEmailVerificationToken.cs");
        var config = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateEmailVerificationTokenConfiguration.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("SHA256.HashData", entity, StringComparison.Ordinal);
        Assert.Contains("TokenHash", entity, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("ConsumedAtUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CanConsume", entity, StringComparison.Ordinal);
        Assert.Contains("Consume(DateTime nowUtc)", entity, StringComparison.Ordinal);

        Assert.Contains("IX_AffiliateEmailVerificationTokens_TokenHash", config, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", config, StringComparison.Ordinal);
        Assert.Contains("AffiliateEmailVerificationTokens", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateIdentity_MustStaySeparateFromCustomerPosAndSuperAdminDomains()
    {
        var authController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var user = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateUser.cs");

        Assert.Contains("IPasswordHasher<AffiliateUser>", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("IPasswordHasher<User>", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubCustomer", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperAdmin", authController, StringComparison.Ordinal);

        Assert.Contains(".AddJwtBearer(\"AffiliateJwt\"", program, StringComparison.Ordinal);
        Assert.Contains("policy.AuthenticationSchemes.Add(\"AffiliateJwt\")", program, StringComparison.Ordinal);
        Assert.Contains("policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, \"Affiliate\")", program, StringComparison.Ordinal);

        Assert.Contains("public class AffiliateUser : BaseEntity, ITenantOwned", user, StringComparison.Ordinal);
        Assert.DoesNotContain(": User", user, StringComparison.Ordinal);
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
