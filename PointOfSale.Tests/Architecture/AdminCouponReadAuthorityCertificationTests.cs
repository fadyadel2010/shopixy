namespace PointOfSale.Tests.Architecture;

public sealed class AdminCouponReadAuthorityCertificationTests
{
    [Fact]
    public void AdminCouponReadEndpoints_MustRemainSqlBackedTenantScopedAndNoStore()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");

        Assert.Contains("[Authorize(Roles = \"Owner,Admin,Manager\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]", controller, StringComparison.Ordinal);
        Assert.Contains("var tenantId = TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("_context.Coupons", controller, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", controller, StringComparison.Ordinal);
        Assert.Contains("c.TenantId == tenantId && !c.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCurrencyAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireCurrency", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICouponReadRepository", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCouponDto_MustCarryBackendReadAuthorityEvidence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");

        Assert.Contains("public Guid TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("public string Code", controller, StringComparison.Ordinal);
        Assert.Contains("public string Name", controller, StringComparison.Ordinal);
        Assert.Contains("public DiscountType DiscountType", controller, StringComparison.Ordinal);
        Assert.Contains("public decimal DiscountValue", controller, StringComparison.Ordinal);
        Assert.Contains("public string Currency", controller, StringComparison.Ordinal);
        Assert.Contains("public int? MaxUsage", controller, StringComparison.Ordinal);
        Assert.Contains("public int CurrentUsage", controller, StringComparison.Ordinal);
        Assert.Contains("public string Status", controller, StringComparison.Ordinal);
        Assert.Contains("public string EligibilitySummary", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveCouponStatus(coupon)", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveEligibilitySummary(coupon)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCouponSearchSortPagination_MustBeBackendExecutedAndFrontendIntentOnly()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CouponsController.cs");
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Coupons.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminCouponsApi.js");

        Assert.Contains("[FromQuery] string? search", controller, StringComparison.Ordinal);
        Assert.Contains("[FromQuery] string sortBy", controller, StringComparison.Ordinal);
        Assert.Contains("[FromQuery] string sortDirection", controller, StringComparison.Ordinal);
        Assert.Contains("Search must be 100 characters or fewer", controller, StringComparison.Ordinal);
        Assert.Contains("ApplyCouponReadSort", controller, StringComparison.Ordinal);
        Assert.Contains("PaginationGuard.NormalizePage", controller, StringComparison.Ordinal);
        Assert.Contains("PaginationGuard.NormalizePageSize", controller, StringComparison.Ordinal);
        Assert.Contains("params.set('search', filters.search)", service, StringComparison.Ordinal);
        Assert.Contains("params.set('sortBy', filters.sortBy)", service, StringComparison.Ordinal);
        Assert.Contains("params.set('sortDirection', filters.sortDirection)", service, StringComparison.Ordinal);
        Assert.Contains("params.set('page', String(filters.page))", service, StringComparison.Ordinal);
        Assert.Contains("'Cache-Control': 'no-store'", service, StringComparison.Ordinal);
        Assert.Contains("listAdminCoupons(filter, user)", page, StringComparison.Ordinal);
        Assert.DoesNotContain(".filter((coupon", page, StringComparison.Ordinal);
        Assert.DoesNotContain(".sort((coupon", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminCouponFrontend_MustAdmitBackendDtoEvidenceBeforePresentation()
    {
        var page = ReadRepoFile("PointOfSale.Web/src/pages/Coupons.jsx");
        var service = ReadRepoFile("PointOfSale.Web/src/services/adminCouponsApi.js");
        var admission = ReadRepoFile("PointOfSale.Web/src/auth/adminCouponReadAuthority.js");

        Assert.Contains("requireAdminCouponListReadAuthority(response.data, currentUser)", service, StringComparison.Ordinal);
        Assert.Contains("requireAdminCouponReadAuthority(response.data, currentUser, couponId)", service, StringComparison.Ordinal);
        Assert.Contains("getAdminCoupon(couponId, user)", page, StringComparison.Ordinal);
        Assert.Contains("formatCurrency(amount, coupon.currency)", page, StringComparison.Ordinal);
        Assert.Contains("coupon.status === 'Inactive'", page, StringComparison.Ordinal);
        Assert.Contains("coupon.status === 'Expired'", page, StringComparison.Ordinal);
        Assert.Contains("selectedCoupon.eligibilitySummary", page, StringComparison.Ordinal);
        Assert.Contains("tenantId.toLowerCase() !== userTenantId.toLowerCase()", admission, StringComparison.Ordinal);
        Assert.Contains("expectedId && couponId.toLowerCase() !== expectedId.toLowerCase()", admission, StringComparison.Ordinal);
        Assert.Contains("seenCouponIds", admission, StringComparison.Ordinal);
        Assert.Contains("Coupon read authority rejected duplicate coupon list evidence", admission, StringComparison.Ordinal);
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
