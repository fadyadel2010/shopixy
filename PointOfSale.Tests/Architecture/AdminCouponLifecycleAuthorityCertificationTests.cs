namespace PointOfSale.Tests.Architecture;

public sealed class AdminCouponLifecycleAuthorityCertificationTests
{
    [Fact]
    public void AdminCouponLifecycleMutations_MustRequireBackendRowVersionAndIfMatch()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("public string RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("RowVersion = GetCouponRowVersion(coupon).ToString", controller, StringComparison.Ordinal);
        Assert.Contains("ParseRequiredCouponIfMatch", controller, StringComparison.Ordinal);
        Assert.Contains("Coupon lifecycle mutations require current RowVersion evidence", controller, StringComparison.Ordinal);
        Assert.Contains("If-Match must contain the current coupon RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("currentRowVersion != expectedRowVersion.RowVersion", controller, StringComparison.Ordinal);
        Assert.Contains("Property(c => c.RowVersion).OriginalValue = expectedRowVersion.RowVersion.Value", controller, StringComparison.Ordinal);
        Assert.Contains("LoadCouponForLifecycleMutationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", controller, StringComparison.Ordinal);
        Assert.Contains("DbUpdateConcurrencyException", controller, StringComparison.Ordinal);
        Assert.Contains("entity.Property(c => c.RowVersion)", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<Coupon>()", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCouponFrontend_MustSubmitLifecycleIntentOnlyWithAcceptedRowVersion()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Coupons.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminCouponsApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/adminCouponReadAuthority.js");

        Assert.Contains("!rowVersion", admission, StringComparison.Ordinal);
        Assert.Contains("Number(rowVersion) <= 0", admission, StringComparison.Ordinal);
        Assert.Contains("mutationOptionsFromCoupon", service, StringComparison.Ordinal);
        Assert.Contains("Coupon lifecycle mutation requires accepted backend RowVersion evidence", service, StringComparison.Ordinal);
        Assert.Contains("'If-Match': `\"${rowVersion}\"`", service, StringComparison.Ordinal);
        Assert.Contains("updateAdminCoupon(editingCoupon, data, user)", page, StringComparison.Ordinal);
        Assert.Contains("toggleAdminCoupon(coupon, user)", page, StringComparison.Ordinal);
        Assert.Contains("archiveAdminCoupon(coupon)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.put(`/coupons/${editingCoupon.id}`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.patch(`/coupons/${id}/toggle`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("api.delete(`/coupons/${id}`", page, StringComparison.Ordinal);
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
