namespace PointOfSale.Tests.Architecture;

public sealed class AdminCouponEligibilityAuthorityCertificationTests
{
    [Fact]
    public void AdminCouponEligibilityMutations_MustValidateAndCanonicalizeOnBackendBeforePersistence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");

        Assert.Contains("ValidateAndCanonicalizeCouponEligibilityAsync", controller, StringComparison.Ordinal);
        Assert.Contains("TryParseCanonicalGuidList", controller, StringComparison.Ordinal);
        Assert.Contains("coupon.ApplicableProductIds = applicableProductJson", controller, StringComparison.Ordinal);
        Assert.Contains("coupon.ApplicableCategoryIds = applicableCategoryJson", controller, StringComparison.Ordinal);
        Assert.Contains("coupon.ExcludedProductIds = excludedProductJson", controller, StringComparison.Ordinal);
        Assert.Contains("A product cannot be both applicable and excluded", controller, StringComparison.Ordinal);
        Assert.Contains("_context.Products", controller, StringComparison.Ordinal);
        Assert.Contains("_context.Categories", controller, StringComparison.Ordinal);
        Assert.Contains("product.TenantId == coupon.TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("category.TenantId == coupon.TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("Coupon product eligibility must reference existing products owned by the current tenant", controller, StringComparison.Ordinal);
        Assert.Contains("Coupon category eligibility must reference existing categories owned by the current tenant", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCouponEligibilityFrontend_MustSubmitCandidatesOnlyAndPresentAdmittedDtoEvidence()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Coupons.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminCouponsApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/adminCouponReadAuthority.js");

        Assert.Contains("createAdminCoupon(data, user)", page, StringComparison.Ordinal);
        Assert.Contains("updateAdminCoupon(editingCoupon, data, user)", page, StringComparison.Ordinal);
        Assert.Contains("requireAdminCouponReadAuthority(response.data, currentUser", service, StringComparison.Ordinal);
        Assert.Contains("requireAdminCouponListReadAuthority(response.data, currentUser)", service, StringComparison.Ordinal);
        Assert.Contains("selectedCoupon.eligibilitySummary", page, StringComparison.Ordinal);
        Assert.Contains("eligibilitySummary", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", page, StringComparison.OrdinalIgnoreCase);
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
