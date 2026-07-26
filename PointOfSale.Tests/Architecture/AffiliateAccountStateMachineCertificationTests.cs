namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateAccountStateMachineCertificationTests
{
    [Fact]
    public void AffiliateAccountLifecycle_MustRemainBackendOwnedAndTerminalClosed()
    {
        var account = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateAccount.cs");
        var configuration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateAccountConfiguration.cs");

        Assert.Contains("public enum AffiliateStatus", account, StringComparison.Ordinal);
        Assert.Contains("Pending = 0", account, StringComparison.Ordinal);
        Assert.Contains("Active = 1", account, StringComparison.Ordinal);
        Assert.Contains("Suspended = 2", account, StringComparison.Ordinal);
        Assert.Contains("Closed = 3", account, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive => Status == AffiliateStatus.Active", account, StringComparison.Ordinal);

        Assert.Contains("if (Status == AffiliateStatus.Closed)", account, StringComparison.Ordinal);
        Assert.Contains("Closed affiliate accounts cannot be reactivated", account, StringComparison.Ordinal);
        Assert.Contains("Only active affiliate accounts can be suspended", account, StringComparison.Ordinal);
        Assert.Contains("public void Close(string reason)", account, StringComparison.Ordinal);
        Assert.Contains("Only active affiliate accounts can be closed", account, StringComparison.Ordinal);

        Assert.Contains("IN (0, 1, 2, 3)", configuration, StringComparison.Ordinal);
        Assert.Contains("ClosureReason", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateStateChanges_MustRevokeCurrentSessionAuthority()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var admin = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("account.Suspend(reason)", service, StringComparison.Ordinal);
        Assert.Contains("await RevokeAffiliateAccountSessionsAsync(affiliateAccountId, nowUtc, ct)", service, StringComparison.Ordinal);
        Assert.Contains("user.RevokeSessions(nowUtc)", service, StringComparison.Ordinal);
        Assert.Contains("account.Close(reason)", service, StringComparison.Ordinal);

        Assert.Contains("Task CloseAffiliateAsync(Guid affiliateAccountId, string reason", service, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"{id:guid}/close\")]", admin, StringComparison.Ordinal);
        Assert.Contains("await _affiliateService.CloseAffiliateAsync(id, request.Reason, ct)", admin, StringComparison.Ordinal);
        Assert.Contains("[FromBody] AdminCloseRequest request", admin, StringComparison.Ordinal);

        Assert.Contains("!affiliateAuthority.AccountActive", program, StringComparison.Ordinal);
        Assert.Contains("affiliateAuthority.AccountDeleted", program, StringComparison.Ordinal);
        Assert.Contains("issuedAtUtc <= affiliateAuthority.TokensValidAfterUtc.Value", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateBusinessCapabilities_MustEnterThroughAffiliateOnlyCurrentSqlAuthority()
    {
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");
        var analytics = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAnalyticsController.cs");
        var links = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs");
        var auth = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliateAuthController.cs");
        var register = Slice(auth, "[HttpPost(\"register\")]", "[HttpPost(\"login\")]");
        var verify = Slice(auth, "[HttpPost(\"verify-email\")]", "[HttpPost(\"resend-verification\")]");

        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", portal, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", analytics, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = \"AffiliateOnly\")]", links, StringComparison.Ordinal);

        Assert.Contains("[HttpGet(\"profile\")]", portal, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"profile/payout-wallet\")]", portal, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"dashboard\")]", portal, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"earnings-summary\")]", portal, StringComparison.Ordinal);
        Assert.Contains("ResolveAffiliateAccountId(ct)", portal, StringComparison.Ordinal);

        Assert.Contains("if (!affiliateUser.EmailVerified)", auth, StringComparison.Ordinal);
        Assert.Contains("!affiliateUser.AffiliateAccount.IsActive", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("affiliateAccount.Activate()", register, StringComparison.Ordinal);
        Assert.Contains("token.AffiliateUser.VerifyEmail()", verify, StringComparison.Ordinal);
        Assert.Contains("token.AffiliateUser.AffiliateAccount?.Status == AffiliateStatus.Pending", verify, StringComparison.Ordinal);
        Assert.Contains("token.AffiliateUser.AffiliateAccount.Activate()", verify, StringComparison.Ordinal);
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
