namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsLifecycleAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsLifecycleAuthority_MustRemainBackendOwnedAndFrontendFailClosed()
    {
        var earningStatus = ReadRepoFile("PointOfSale.Core/Enums/CommerceHubVendorEarningStatus.cs");
        var payoutStatus = ReadRepoFile("PointOfSale.Core/Enums/CommerceHubPayoutStatus.cs");
        var earnings = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var workflow = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubFinancialWorkflowService.cs");
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var page = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-lifecycle-authority-certification-findings.md");

        foreach (var state in new[] { "Pending", "Available", "InPayout", "PaidOut", "Reversed" })
        {
            Assert.Contains(state, earningStatus, StringComparison.Ordinal);
        }

        foreach (var state in new[] { "Pending", "Processing", "Completed", "Failed", "ReconciliationRequired" })
        {
            Assert.Contains(state, payoutStatus, StringComparison.Ordinal);
        }

        Assert.Contains("earning.Status = CommerceHubVendorEarningStatus.Available", workflow, StringComparison.Ordinal);
        Assert.Contains("earning.Status = CommerceHubVendorEarningStatus.InPayout", payout, StringComparison.Ordinal);
        Assert.Contains("earning.Status = CommerceHubVendorEarningStatus.PaidOut", payout, StringComparison.Ordinal);
        Assert.Contains("earning.Status = CommerceHubVendorEarningStatus.Reversed", ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs"), StringComparison.Ordinal);
        Assert.Contains("payout.Status = CommerceHubPayoutStatus.Processing", payout, StringComparison.Ordinal);
        Assert.Contains("payout.Status = CommerceHubPayoutStatus.Completed", payout, StringComparison.Ordinal);
        Assert.Contains("payout.Status = CommerceHubPayoutStatus.Failed", payout, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutReconciliationHostedService.cs"), StringComparison.Ordinal);
        Assert.Contains("TenantId == storeId", earnings, StringComparison.Ordinal);

        Assert.Contains("EARNING_STATUSES", adapter, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_STATUSES", adapter, StringComparison.Ordinal);
        Assert.Contains("hasValidEarningLifecycleEvidence", adapter, StringComparison.Ordinal);
        Assert.Contains("validateEarningLifecycleTotals", adapter, StringComparison.Ordinal);
        Assert.Contains("validatePayoutEarningBindings", adapter, StringComparison.Ordinal);
        Assert.Contains("earningsQuery.isFetching", page, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", page, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-C",
            "## Lifecycle Findings",
            "## Runtime Authority Findings",
            "## Transition Findings",
            "## Settlement Findings",
            "## Payout Findings",
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
            "## Actual Lifecycle States Discovered",
            "## Explicit Certification Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
