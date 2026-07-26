namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsCurrencyAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsRuntime_MustRequireEgpOnlyAcrossEarningsFinanceStatementsAndPayouts()
    {
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var financeDashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var ledgerService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");
        var safetyGuard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialSafetyGuardService.cs");
        var refundDecision = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundFinancialDecisionEngine.cs");
        var financialTransaction = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs");
        var settlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");
        var paymentRequest = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorPaymentRequest.cs");

        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", earningsService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", financeDashboard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", payoutRead, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", payoutService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", ledgerService, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", refundDecision, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", financialTransaction, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", settlement, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", paymentRequest, StringComparison.Ordinal);

        Assert.DoesNotContain("FinancialCurrencyGuard.RequireCurrency(\r\n            currency,\r\n            \"commercehub.financial_transaction.currency\"", financialTransaction, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = tenant.Currency", payoutRead, StringComparison.Ordinal);
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");
        var vendorPayments = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs");
        Assert.DoesNotContain("Currency = tenant.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.DoesNotContain("currency ?? vendor.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant.Currency))", vendorPayments, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorEarningsPersistence_MustFenceNonEgpDurableFinancialRows()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var financialTransactionConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubFinancialTransactionConfiguration.cs");
        var settlementConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorSettlementConfiguration.cs");
        var paymentRequestConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubVendorPaymentRequestConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening.cs");

        Assert.Contains("GuardCommerceHubCurrencySovereignty", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Currency_EGP", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubFinancialTransactions_Currency_EGP", financialTransactionConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubVendorSettlements_Currency_EGP", settlementConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubVendorPaymentRequests_Currency_EGP", paymentRequestConfiguration, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "CK_CommerceHubPayouts_Currency_EGP",
            "CK_CommerceHubFinancialTransactions_Currency_EGP",
            "CK_CommerceHubVendorSettlements_Currency_EGP",
            "CK_CommerceHubVendorPaymentRequests_Currency_EGP",
            "NOT VALID"
        })
        {
            Assert.Contains(marker, migration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VendorEarningsFrontendFormatters_MustBlockNonEgpPresentation()
    {
        var vendorEarnings = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var vendorFinance = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var payoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");
        var payoutPage = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");
        var vendorFinanceWorkspace = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/VendorFinanceWorkspace.tsx");
        var financeCharts = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/FinanceCharts.tsx");
        var financeAlerts = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/FinanceAlertsPanel.tsx");
        var financeKpis = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/FinanceKpiCards.tsx");

        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", vendorEarnings, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", vendorFinance, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'", payoutAuthority, StringComparison.Ordinal);
        Assert.Contains("authorityCurrency(item.currency)", payoutAuthority, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", payoutPage, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", vendorFinanceWorkspace, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", financeCharts, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", financeAlerts, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", financeKpis, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
