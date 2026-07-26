namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPayoutEligibilityCurrencyAuthorityCertificationTests
{
    [Fact]
    public void PayoutEligibilityCurrency_MustRemainCommerceHubOwnedAndEgpOnly()
    {
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var payoutController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        var safetyGuard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialSafetyGuardService.cs");
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var frontendPayoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");

        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", payoutRead, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", payoutRead, StringComparison.Ordinal);
        Assert.Contains("eligibleAmount = 0m", payoutRead, StringComparison.Ordinal);
        Assert.Contains("RequireQueueSnapshotCurrencyAuthority", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("commercehub.vendor_payout_queue.snapshot.currency", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_ELIGIBILITY_CURRENCY_INVALID", superAdminPayouts, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorFinancialControlException exception", payoutController, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", earningsService, StringComparison.Ordinal);
        Assert.Contains("const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'", frontendPayoutAuthority, StringComparison.Ordinal);
        Assert.Contains("authorityCurrency(item.currency)", frontendPayoutAuthority, StringComparison.Ordinal);
        Assert.Contains("authorityCurrency(summary?.currency)", frontendPayoutAuthority, StringComparison.Ordinal);
        Assert.Contains("authorityCurrency(payout.currency)", frontendPayoutAuthority, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutEligibilityCurrency_MustNotInheritTenantVendorStorefrontPosOrDtoCurrency()
    {
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var superAdminPayouts = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var payoutsService = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutsService.ts");
        var frontendPayoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");

        Assert.DoesNotContain("Currency = tenant.Currency", payoutRead, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = vendor.Currency", payoutRead, StringComparison.Ordinal);
        Assert.DoesNotContain("Tenant.Currency", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("request.Currency", superAdminPayouts, StringComparison.Ordinal);
        Assert.DoesNotContain("currency ??", frontendPayoutAuthority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPayoutQueueFinancialAuthority", payoutsService, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPayoutExecutionPresentation", payoutsService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
