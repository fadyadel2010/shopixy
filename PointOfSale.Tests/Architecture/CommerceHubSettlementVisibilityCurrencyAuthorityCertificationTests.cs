namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSettlementVisibilityCurrencyAuthorityCertificationTests
{
    [Fact]
    public void SettlementVisibilityCurrency_MustRemainEgpOnlyAcrossScreensReportsExportsAndFormatters()
    {
        var vendorFrontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var vendorFinancePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var superAdminFinancePage = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/FinanceDashboardPage.tsx");
        var superAdminVendorWorkspace = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/VendorFinanceWorkspace.tsx");
        var financialDashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var superAdminReports = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.cs");
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");

        Assert.Contains("const hasAuthorityCurrency = (value) => normalizeString(value) === 'EGP'", vendorFrontendAuthority, StringComparison.Ordinal);
        Assert.Contains("!hasAuthorityCurrency(scopedValue.currency)", vendorFrontendAuthority, StringComparison.Ordinal);
        Assert.Contains("!hasSingleAuthorityCurrency(scopedValue.currency, scopedValue.transactions ?? [])", vendorFrontendAuthority, StringComparison.Ordinal);
        Assert.Contains("resolveCommerceHubDisplayCurrency(currency)", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("currency: COMMERCEHUB_AUTHORITY_CURRENCY", superAdminFinancePage, StringComparison.Ordinal);
        Assert.Contains("CommerceHub currency blocked", superAdminVendorWorkspace, StringComparison.Ordinal);
        Assert.Contains("Currency = RequireEgpCurrency(", financialDashboard, StringComparison.Ordinal);
        Assert.Contains("commercehub.vendor_statement.csv.row.currency", financialDashboard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency(currency", superAdminReports, StringComparison.Ordinal);
        Assert.Contains("var currency = NormalizeCurrency(row.Currency);", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("Message = BuildAlertMessage(row, currency)", superAdminVendorFinance, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementVisibilityCurrency_MustNotInheritTenantVendorBrowserProjectionCacheOrLocaleCurrency()
    {
        var vendorFrontendAuthority = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var vendorFinancePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var superAdminVendorWorkspace = ReadRepoFile("PointOfSale.Web/src/super-admin/finance/VendorFinanceWorkspace.tsx");
        var superAdminReports = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminFinancialDashboardService.cs");
        var superAdminVendorFinance = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminVendorFinanceService.cs");

        Assert.DoesNotContain("settings.currency", vendorFinancePage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("useSettings", vendorFinancePage, StringComparison.Ordinal);
        Assert.DoesNotContain("Intl.NumberFormat(undefined", vendorFinancePage, StringComparison.Ordinal);
        Assert.DoesNotContain("Intl.NumberFormat(locale", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("currency: normalizedCurrency", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("currency: COMMERCEHUB_AUTHORITY_CURRENCY", superAdminVendorWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = tenant.Currency", superAdminReports, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = tenant.Currency", superAdminVendorFinance, StringComparison.Ordinal);
        Assert.Contains("queryKey: [...vendorSessionScope, 'commercehub-finance', 'statement', statementFilters]", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorFinanceStatementPresentation(statementQuery.data, user)", vendorFinancePage, StringComparison.Ordinal);
        Assert.Contains("validateStatementPagination(scopedValue)", vendorFrontendAuthority, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
