namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsScopeIsolation_MustRemainBackendOwnedAndFailClosed()
    {
        var earningsController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var financeService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-scope-isolation-authority-certification-findings.md");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", earningsController, StringComparison.Ordinal);
        Assert.Contains("_tenantContext.TenantId", earningsController, StringComparison.Ordinal);
        Assert.Contains("earning.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("payout.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)", financeService, StringComparison.Ordinal);
        Assert.Contains("VendorId = vendorId", financeService, StringComparison.Ordinal);

        Assert.Contains("validateRecentSettlements", adapter, StringComparison.Ordinal);
        Assert.Contains("validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(row, currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow'", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-G",
            "## Scope Findings",
            "## Runtime Authority Findings",
            "## Vendor Findings",
            "## Actor Findings",
            "## Tenant Findings",
            "## Payout Findings",
            "## Settlement Findings",
            "## Async Findings",
            "## Replay Findings",
            "## Cache Findings",
            "## Failure Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Explicit Certification Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
