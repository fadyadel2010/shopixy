namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubReconciliationCurrencyAuthorityCertificationTests
{
    [Fact]
    public void ReconciliationRecoveryReplayRepairAndRebuildCurrency_MustRemainCommerceHubOwnedAndEgpOnly()
    {
        var payoutReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var financeReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationReadService.cs");
        var unresolvedPaymentRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var financialTransaction = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs");
        var vendorSettlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");

        Assert.Contains("commercehub.payout.reconciliation.currency", payoutReconciliation, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", payoutReconciliation, StringComparison.Ordinal);
        Assert.Contains("Currency = reconciliationCurrency", payoutReconciliation, StringComparison.Ordinal);

        Assert.Contains("var egpLedger = ledger.Where", financeReconciliation, StringComparison.Ordinal);
        Assert.Contains("transaction.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode", financeReconciliation, StringComparison.Ordinal);
        Assert.Contains("group.Any(transaction => transaction.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode)", financeReconciliation, StringComparison.Ordinal);
        Assert.Contains("!IsEgpCurrency(group.Currency)", financeReconciliation, StringComparison.Ordinal);
        Assert.Contains("Currency = group.Key.Currency", financeReconciliation, StringComparison.Ordinal);

        Assert.Contains("commercehub.unresolved_payment_recovery.webhook.currency", unresolvedPaymentRecovery, StringComparison.Ordinal);
        Assert.Contains("commercehub.unresolved_payment_recovery.payment.currency", unresolvedPaymentRecovery, StringComparison.Ordinal);

        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutRead, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", payoutRead, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardCommerceHubCurrencySovereignty", dbContext, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", financialTransaction, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", vendorSettlement, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationCurrency_MustNotInheritTenantVendorStorefrontPosDtoProviderCacheOrProjectionCurrency()
    {
        var payoutReconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs");
        var unresolvedPaymentRecovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinanceReconciliationJob.cs");
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var frontendPayoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");
        var vendorFrontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");

        Assert.DoesNotContain("Tenant.Currency", payoutReconciliation, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor.Currency", payoutReconciliation, StringComparison.Ordinal);
        Assert.DoesNotContain("storefront.Currency", payoutReconciliation, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = webhook.Currency", unresolvedPaymentRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = payment.Currency", unresolvedPaymentRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.Currency", payoutRead, StringComparison.Ordinal);
        Assert.Contains("Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutRead, StringComparison.Ordinal);
        Assert.Contains("const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'", frontendPayoutAuthority, StringComparison.Ordinal);
        Assert.Contains("const hasAuthorityCurrency = (value) => normalizeString(value) === 'EGP'", vendorFrontendAuthority, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
