namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorEarningsIdentityAuthorityCertificationTests
{
    [Fact]
    public void VendorEarningsIdentityAuthority_MustRemainBackendOwnedAndFrontendDerivativeOnly()
    {
        var adapter = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorFinancialParticipationAuthority.js");
        var earningsPage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubEarnings.jsx");
        var financePage = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubFinance.jsx");
        var bootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubOrderDtos.cs");
        var payoutDto = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubFinanceDtos.cs");
        var findings = ReadRepoFile("docs/architecture/vendor-earnings-identity-authority-certification-findings.md");

        Assert.Contains("public Guid VendorId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid TenantId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid EarningId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid SubOrderId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid? OriginalEarningId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid? RefundId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid? PayoutId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid PayoutItemId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("public Guid PayoutId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("public Guid TenantId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("public Guid EarningId", payoutDto, StringComparison.Ordinal);
        Assert.Contains("public Guid SubOrderId", payoutDto, StringComparison.Ordinal);

        Assert.Contains("hasCurrentVendorAuthority(scopedValue, currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("validatePayoutItems", adapter, StringComparison.Ordinal);
        Assert.Contains("validateEarningItems", adapter, StringComparison.Ordinal);
        Assert.Contains("validatePayoutEarningBindings", adapter, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(row, currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("hasCurrentVendorAuthority(item, currentUser)", adapter, StringComparison.Ordinal);
        Assert.Contains("EARNING_STATUSES", adapter, StringComparison.Ordinal);
        Assert.Contains("GUID_PATTERN.test(normalizeString(item?.earningId))", adapter, StringComparison.Ordinal);
        Assert.Contains("GUID_PATTERN.test(normalizeString(item?.subOrderId))", adapter, StringComparison.Ordinal);
        Assert.Contains("sameGuid(item?.payoutId, payoutId)", adapter, StringComparison.Ordinal);
        Assert.Contains("hasValidEarningLifecycleEvidence", adapter, StringComparison.Ordinal);
        Assert.Contains("validateEarningLifecycleTotals", adapter, StringComparison.Ordinal);
        Assert.Contains("earningStatus === 'InPayout'", adapter, StringComparison.Ordinal);
        Assert.Contains("payoutStatus === 'Completed'", adapter, StringComparison.Ordinal);

        Assert.Contains("queryKey: [...vendorSessionScope, 'commercehub-earnings-dashboard']", earningsPage, StringComparison.Ordinal);
        Assert.Contains("earningsQuery.isFetching", earningsPage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", earningsPage, StringComparison.Ordinal);
        Assert.Contains("queryKey: [...vendorSessionScope, 'commercehub-finance', 'earnings-dashboard']", financePage, StringComparison.Ordinal);
        Assert.Contains("deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)", financePage, StringComparison.Ordinal);
        Assert.Contains("predicate: (query)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("root === 'commercehub-vendor-session'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("domain === 'commercehub-earnings-dashboard'", bootstrap, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "PASS M6-A",
            "## Identity Findings",
            "## Runtime Authority Findings",
            "## Replay Findings",
            "## Lineage Findings",
            "## Cache Findings",
            "## Tenant Findings",
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
