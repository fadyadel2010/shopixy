namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsOwnershipAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsOwnershipAuthority_MustRemainBackendOwnedAndFrontendFailClosed()
    {
        var earningsDto = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs");
        var payoutDto = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubFinanceDtos.cs");
        var earningsService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubEarningsService.cs");
        var payoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-ownership-authority-certification-findings.md");

        Assert.Contains("public Guid TenantId", earningsDto, StringComparison.Ordinal);
        Assert.Contains("public Guid TenantId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("public Guid PayoutId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("earning.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("payout.TenantId == storeId", earningsService, StringComparison.Ordinal);
        Assert.Contains("TenantId = earning.TenantId", earningsService, StringComparison.Ordinal);
        Assert.Contains("TenantId = payout.TenantId", earningsService, StringComparison.Ordinal);
        Assert.Contains("PayoutId = payout.Id", earningsService, StringComparison.Ordinal);
        Assert.Contains("TenantId = payout.TenantId", payoutService, StringComparison.Ordinal);
        Assert.Contains("PayoutId = payout.Id", payoutService, StringComparison.Ordinal);

        Assert.Contains("hasCurrentVendorAuthority(item, currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("sameGuid(item?.payoutId, payoutId)", adapter, StringComparison.Ordinal);
        Assert.Contains("validatePayoutEarningBindings", adapter, StringComparison.Ordinal);
        Assert.Contains("sameGuid(earning.subOrderId, item.subOrderId)", adapter, StringComparison.Ordinal);
        Assert.Contains("hasValidEarningLifecycleEvidence", adapter, StringComparison.Ordinal);
        Assert.Contains("validateEarningLifecycleTotals", adapter, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pageshow', handlePageShow)", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-B",
            "## Ownership Findings",
            "## Runtime Authority Findings",
            "## Vendor Findings",
            "## Actor Findings",
            "## Tenant Findings",
            "## Payout Findings",
            "## Settlement Findings",
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
