namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateOrderLineageAdminPresentationAuthorityCertificationTests
{
    [Fact]
    public void AdminOrderDtos_MustExposeBackendAdmittedAffiliateEvidenceOnly()
    {
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Admin/AdminOrderDtos.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Admin/AdminOrderService.cs");

        Assert.Contains("public AdminAffiliateAttributionDto? AffiliateAttribution", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid AttributionId", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid AffiliateAccountId", dto, StringComparison.Ordinal);
        Assert.Contains("public string ReferralCode", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid? PromotionLinkId", dto, StringComparison.Ordinal);
        Assert.Contains("AttachAffiliateAttributionEvidenceAsync", service, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateAttributionEvidenceAsync", service, StringComparison.Ordinal);
        Assert.Contains("_context.AffiliateAttributions.AsNoTracking()", service, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateAccountId.Value == attribution.AffiliateAccountId", service, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateReferralCode == attribution.ReferralCode", service, StringComparison.Ordinal);
        Assert.Contains("order.PromotionLinkId == attribution.PromotionLinkId", service, StringComparison.Ordinal);
        Assert.Contains("AffiliateAttribution = null", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendOrders_MustAdmitAffiliateEvidenceBeforeRendering()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Orders.jsx");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js");

        Assert.Contains("requireBackendAffiliateAttributionAuthority", page, StringComparison.Ordinal);
        Assert.Contains("getAffiliateAttributionPresentation(order)", page, StringComparison.Ordinal);
        Assert.Contains("Affiliate Attribution", page, StringComparison.Ordinal);
        Assert.Contains("Affiliate ${affiliateAttribution.referralCode}", page, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_ATTRIBUTION_SOURCES", admission, StringComparison.Ordinal);
        Assert.Contains("cross-order attribution evidence", admission, StringComparison.Ordinal);
        Assert.Contains("incomplete attribution evidence", admission, StringComparison.Ordinal);
        Assert.Contains("admitBackendAffiliateAttributionPresentation(scopedValue)", admission, StringComparison.Ordinal);
        Assert.Contains("acceptedOrders.push(admitBackendAffiliateAttributionPresentation(scopedOrder))", admission, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_ATTRIBUTION_WARNING_CODE", admission, StringComparison.Ordinal);
        Assert.Contains("Affiliate attribution is unavailable for this order.", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", page, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliateAttributionRecovery_MustRemainSqlSnapshotOwned()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var affiliateService = ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var attributionConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateAttributionConfiguration.cs");

        Assert.Contains("AffiliateAttribution.CreateV2", checkout, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateAccountId = resolvedAffiliateAuthority.AffiliateAccountId", checkout, StringComparison.Ordinal);
        Assert.Contains("order.AffiliateReferralCode = resolvedAffiliateAuthority.ReferralCode", checkout, StringComparison.Ordinal);
        Assert.Contains("AttributeOrderToAffiliateFromOrderSnapshotAsync", affiliateService, StringComparison.Ordinal);
        Assert.Contains("TryRepairMissingAttributionAsync", affiliateService, StringComparison.Ordinal);
        Assert.Contains("GuardAffiliateAttributionAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be changed after conversion", dbContext, StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliateAttributions_Tenant_Order", attributionConfig, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
