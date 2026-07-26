namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsFinancialAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsFinancialAuthority_MustRemainBackendOwnedAndFrontendFailClosed()
    {
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var payoutReadService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-financial-authority-certification-findings.md");

        Assert.Contains("TotalPayable = payoutSnapshot.TotalPayable", earningsService, StringComparison.Ordinal);
        Assert.Contains("EligiblePayoutAmount = payoutSnapshot.EligibleAmount", earningsService, StringComparison.Ordinal);
        Assert.Contains("HoldbackAmount = payoutSnapshot.HoldbackAmount", earningsService, StringComparison.Ordinal);
        Assert.Contains("totalPayable = RoundCurrency", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("eligibleAmount = RoundCurrency", payoutReadService, StringComparison.Ordinal);
        Assert.Contains("holdbackAmount = RoundCurrency", payoutReadService, StringComparison.Ordinal);

        Assert.Contains("validatePayoutReadinessTotals", adapter, StringComparison.Ordinal);
        Assert.Contains("hasSingleAuthorityCurrency", adapter, StringComparison.Ordinal);
        Assert.Contains("validateEarningLifecycleTotals", adapter, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", earningsPage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorFinancialDashboardPresentation(dashboardQuery.data, user)", financePage, StringComparison.Ordinal);
        Assert.DoesNotContain("createPayout", earningsPage, StringComparison.OrdinalIgnoreCase);

        foreach (var marker in new[]
        {
            "PASS M6-F",
            "## Financial Findings",
            "## Runtime Authority Findings",
            "## Calculation Findings",
            "## Dashboard Findings",
            "## Statement Findings",
            "## Payout Findings",
            "## Settlement Findings",
            "## Lineage Findings",
            "## Replay Findings",
            "## Cache Findings",
            "## Tenant Findings",
            "## Actor Findings",
            "## Failure Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Actual Financial Artifacts Discovered",
            "## Explicit Certification Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
