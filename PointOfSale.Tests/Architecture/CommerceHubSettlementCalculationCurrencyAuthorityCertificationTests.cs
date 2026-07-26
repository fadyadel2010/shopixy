namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSettlementCalculationCurrencyAuthorityCertificationTests
{
    [Fact]
    public void SettlementCalculationCurrency_MustRemainEgpOnlyAcrossBalancesStatementsLedgerAndPayoutEligibility()
    {
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var financeDashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var ledgerService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");

        Assert.Contains("FinancialCurrencyGuard.PlatformBaseCurrencyCode", payoutRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", payoutRead, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", payoutRead, StringComparison.Ordinal);
        Assert.Contains("eligibleAmount = 0m", payoutRead, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureLedgerQueryUsesEgpCurrencyAsync", financeDashboard, StringComparison.Ordinal);
        Assert.Contains("commercehub.vendor_statement.csv.row.currency", financeDashboard, StringComparison.Ordinal);
        Assert.Contains("CalculateStatementOpeningBalanceAsync", financeDashboard, StringComparison.Ordinal);
        Assert.Contains("BuildRunningBalancesAsync", financeDashboard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", ledgerService, StringComparison.Ordinal);
        Assert.Contains("ValidateOrderFinancialIntegrity", ledgerService, StringComparison.Ordinal);
        Assert.Contains("ValidateRefundFinancialIntegrity", ledgerService, StringComparison.Ordinal);
        Assert.Contains("ValidatePayoutFinancialIntegrity", ledgerService, StringComparison.Ordinal);
        Assert.Contains("RequireEgpCurrency(", earningsService, StringComparison.Ordinal);
        Assert.Contains("Currency = NormalizeCurrency(row.Currency)", superAdminVendorFinance, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementCalculations_MustNotInheritTenantVendorStorefrontPosOrBrowserCurrency()
    {
        var payoutRead = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var financeDashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");
        var frontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");

        Assert.DoesNotContain("Currency = tenant.Currency", payoutRead, StringComparison.Ordinal);
        Assert.DoesNotContain("currency ?? vendor.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.DoesNotContain("Intl.NumberFormat(undefined", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("const hasAuthorityCurrency = (value) => normalizeString(value) === 'EGP'", frontendAuthority, StringComparison.Ordinal);
        Assert.Contains("EnsureSingleCurrency(", financeDashboard, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
