namespace PointOfSale.Tests.Architecture;

public sealed class SmartOfferGeneratedCouponSyncAuthorityTests
{
    [Fact]
    public void GeneratedCouponSync_MustRepairProjectionWithoutAuthoritativeCouponEvent()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminSmartOffersController.cs");
        var syncStart = controller.IndexOf("SyncGeneratedCouponsToMongo", StringComparison.Ordinal);
        var syncEnd = controller.IndexOf("private async Task<List<AdminSmartOfferDto>>", syncStart, StringComparison.Ordinal);

        Assert.True(syncStart >= 0, "Expected generated coupon sync endpoint to exist.");
        Assert.True(syncEnd > syncStart, "Expected generated coupon sync method boundary to be discoverable.");

        var syncMethod = controller[syncStart..syncEnd];
        Assert.Contains("RebuildCouponsAsync", syncMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishCouponUpdatedEventAsync", syncMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", syncMethod, StringComparison.Ordinal);
    }
}
