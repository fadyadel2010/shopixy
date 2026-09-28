namespace PointOfSale.Tests.Architecture;

public sealed class OrderDomainIsolationCertificationTests
{
    [Fact]
    public void StorefrontAndCommerceHubOrders_MustUseSeparateAdmissionContracts()
    {
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminOrdersController.cs");
        var commerceHubController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/CommerceHubOrdersController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");

        Assert.Contains("Source = OrderSource.Ecommerce", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ORDER_SCOPE_INVALID", storefrontController, StringComparison.Ordinal);
        Assert.Contains("api/commercehub/vendor-orders", commerceHubController, StringComparison.Ordinal);
        Assert.Contains("Source = OrderSource.Marketplace", commerceHubController, StringComparison.Ordinal);
        Assert.Contains("GetCommerceHubVendorOrderDetailsAsync", commerceHubController, StringComparison.Ordinal);
        Assert.Contains("query.Source ??= OrderSource.Ecommerce", service, StringComparison.Ordinal);
        Assert.Contains("query.Source is not OrderSource.Ecommerce and not OrderSource.Marketplace", service, StringComparison.Ordinal);
        Assert.Contains("o.Source == OrderSource.Ecommerce", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateOrderScreens_MustAdmitCanonicalEcommerceAttributionBeforeProjectionPaging()
    {
        var admin = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var portal = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AffiliatePortalController.cs");

        AssertCanonicalAffiliateAdmission(admin);
        AssertCanonicalAffiliateAdmission(portal);
    }

    [Fact]
    public void AttributionOrders_MustApplyCanonicalMembershipAndFiltersBeforeCountAndPaging()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs");
        var store = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs");

        Assert.Contains("GetAttributionPageAsync", controller, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderIds.Distinct()", store, StringComparison.Ordinal);
        Assert.Contains("filterBuilder.In(x => x.OrderId, purchaseOrderIds)", store, StringComparison.Ordinal);

        var count = store.IndexOf("CountDocumentsAsync(filter", StringComparison.Ordinal);
        var skip = store.IndexOf(".Skip(skip)", count, StringComparison.Ordinal);
        Assert.True(count >= 0 && skip > count, "Attribution count and canonical filters must run before page slicing.");
    }

    private static void AssertCanonicalAffiliateAdmission(string source)
    {
        Assert.Contains("order.Source == OrderSource.Ecommerce", source, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttributions.IgnoreQueryFilters()", source, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateReferralCode == attribution.ReferralCode", source, StringComparison.Ordinal);
        Assert.Contains("order.PromotionLinkId == attribution.PromotionLinkId", source, StringComparison.Ordinal);

        var canonicalIds = source.IndexOf("canonicalOrderIds", StringComparison.Ordinal);
        var count = source.IndexOf("CountDocumentsAsync(filter", canonicalIds, StringComparison.Ordinal);
        Assert.True(canonicalIds >= 0 && count > canonicalIds, "Canonical Affiliate membership must be resolved before projection count and paging.");
    }
}
