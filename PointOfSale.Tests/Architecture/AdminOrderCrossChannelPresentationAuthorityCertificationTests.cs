namespace PointOfSale.Tests.Architecture;

public sealed class AdminOrderCrossChannelPresentationAuthorityCertificationTests
{
    [Fact]
    public void SharedAdminOrderPresentation_MustPreserveBackendOwnedChannelIdentity()
    {
        var dto = CertificationSource.ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");
        var ordersApi = CertificationSource.ReadRepoFile("PointOfSale.Web/src/services/adminOrdersApi.js");
        var ordersPage = CertificationSource.ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");

        Assert.Contains("public OrderSource Source", dto, StringComparison.Ordinal);
        Assert.Contains("public string? SourceOrderId", dto, StringComparison.Ordinal);
        Assert.Contains("public AdminAffiliateAttributionDto? AffiliateAttribution", dto, StringComparison.Ordinal);
        Assert.Contains("public AdminMarketplaceLineageDto? MarketplaceLineage", dto, StringComparison.Ordinal);

        Assert.Contains("Source = o.Source", service, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId = o.SourceOrderId", service, StringComparison.Ordinal);
        Assert.Contains("Source = order.Source", service, StringComparison.Ordinal);
        Assert.Contains("Source = OrderSource.Marketplace", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceOrderId = subOrder.MarketplaceOrderId", service, StringComparison.Ordinal);
        Assert.Contains("SourceOrderId = row.SourceOrderId ?? row.MarketplaceOrderId?.ToString()", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttribution = null", service, StringComparison.Ordinal);
        Assert.Contains("MarketplaceLineage = null", service, StringComparison.Ordinal);
        Assert.Contains("AttachAffiliateAttributionEvidenceAsync", service, StringComparison.Ordinal);
        Assert.Contains("AttachMarketplaceLineageEvidenceAsync", service, StringComparison.Ordinal);

        Assert.Contains("requireVendorOrderVisibilityListAuthority", ordersApi, StringComparison.Ordinal);
        Assert.Contains("requireVendorOrderVisibilityAuthority", ordersApi, StringComparison.Ordinal);
        Assert.Contains("requireCurrentVendorOrderVisibilityListAuthority(ordersQuery.data, user)", ordersPage, StringComparison.Ordinal);
        Assert.Contains("requireCurrentVendorOrderVisibilityAuthority(detailsQuery.data, user, selectedOrderId)", ordersPage, StringComparison.Ordinal);

        Assert.Contains("requireBackendOrderSourceAuthority(scopedValue)", admission, StringComparison.Ordinal);
        Assert.Contains("requireBackendOrderSourceAuthority(scopedOrder)", admission, StringComparison.Ordinal);
        Assert.Contains("requireBackendAffiliateAttributionAuthority(scopedValue)", admission, StringComparison.Ordinal);
        Assert.Contains("requireBackendMarketplaceLineageAuthority(order)", admission, StringComparison.Ordinal);
        Assert.Contains("sourceOrderId.toLowerCase() !== marketplaceOrderId.toLowerCase()", admission, StringComparison.Ordinal);
        Assert.Contains("Marketplace order lineage authority rejected source identity evidence.", admission, StringComparison.Ordinal);
        Assert.Contains("Marketplace order lineage authority rejected marketplace evidence on a non-marketplace order.", admission, StringComparison.Ordinal);
        Assert.Contains("Affiliate order lineage authority rejected cross-order attribution evidence.", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedAdminOrderPresentationCertificationArtifacts_MustBeFrozen()
    {
        var requiredFiles = new[]
        {
            "docs/architecture/admin-order-cross-channel-presentation-authority-certification-findings.md",
            ".ai-context/PROJECT_CONTEXT.md",
            ".ai-context/ARCHITECTURE_TRUTH.md",
            ".ai-context/KNOWN_RISKS.md",
            ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
            ".ai-context/CODEX_RULES.md"
        };

        foreach (var path in requiredFiles)
        {
            var contents = CertificationSource.ReadRepoFile(path);
            Assert.Contains("PASS SF-ORD.XCHANNEL Admin Order Cross-Channel Presentation Authority", contents, StringComparison.Ordinal);
            Assert.Contains("Storefront Orders remain Storefront", contents, StringComparison.Ordinal);
            Assert.Contains("Affiliate Orders remain Affiliate", contents, StringComparison.Ordinal);
            Assert.Contains("Marketplace Orders remain Marketplace", contents, StringComparison.Ordinal);
            Assert.Contains("Frontend never owns Channel Identity", contents, StringComparison.Ordinal);
        }
    }
}
