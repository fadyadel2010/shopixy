namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsVisibilityAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsVisibilityAuthority_MustRemainBackendOwnedAndFrontendFailClosed()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubFinanceController.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var earningsPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var financePage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var bootstrap = CertificationSource.ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var findings = CertificationSource.ReadRepoFile("docs/architecture/vendor-earnings-visibility-authority-certification-findings.md");

        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("GetVendorStatementAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ExportVendorStatementCsvAsync", controller, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority", adapter, StringComparison.Ordinal);
        Assert.Contains("validateFinanceTransactions", adapter, StringComparison.Ordinal);
        Assert.Contains("validateStatementPagination", adapter, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation", earningsPage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorFinanceStatementPresentation", financePage, StringComparison.Ordinal);
        Assert.Contains("'commercehub-vendor-session', user?.tenantId", financePage, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-D", "## Visibility Findings", "## Runtime Authority Findings", "## Vendor Findings",
            "## Admin Findings", "## Tenant Findings", "## Filter Findings", "## Search Findings",
            "## Pagination Findings", "## Export Findings", "## Replay Findings", "## Cache Findings",
            "## Failure Findings", "## Adversarial Findings", "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified", "## Regression Freezes Added", "## Architecture Updates",
            "## Explicit Certification Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }
}
