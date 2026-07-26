namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsFinalAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsAuthority_MustRemainBackendOwnedAndFrontendDerivativeOnly()
    {
        var earningsController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");
        var financeController = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubFinanceController.cs");
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var payoutReadService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var financeService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-final-authority-certification-findings.md");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", earningsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId", earningsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.RequiredTenantId", financeController, StringComparison.Ordinal);
        Assert.Contains("GetVendorStatementAsync", financeController, StringComparison.Ordinal);
        Assert.Contains("ExportVendorStatementCsvAsync", financeController, StringComparison.Ordinal);

        Assert.Contains("earning.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("payout.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("TotalPayable = payoutSnapshot.TotalPayable", earningsService, StringComparison.Ordinal);
        Assert.Contains("eligibleAmount = RoundCurrency", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)", financeService, StringComparison.Ordinal);
        Assert.Contains("EnsureSingleCurrency", financeService, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "validateEarningItems", "validatePayoutEarningBindings", "validateEarningLifecycleTotals",
            "validatePayoutReadinessTotals", "validateFinanceTransactions", "validateStatementPagination",
            "hasSingleAuthorityCurrency", "hasCurrentVendorAuthority"
        })
        {
            Assert.Contains(marker, adapter, StringComparison.Ordinal);
        }

        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", earningsPage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", financePage, StringComparison.Ordinal);
        Assert.DoesNotContain("createPayout", earningsPage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-H", "## Final Findings", "## Identity Findings", "## Ownership Findings",
            "## Lifecycle Findings", "## Visibility Findings", "## Action Findings", "## Financial Findings",
            "## Scope Findings", "## Cross-Domain Findings", "## Bootstrap Findings", "## Cache Findings",
            "## Failure Findings", "## Adversarial Findings", "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified", "## Regression Freezes Added", "## Architecture Updates",
            "## Explicit Certification Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
