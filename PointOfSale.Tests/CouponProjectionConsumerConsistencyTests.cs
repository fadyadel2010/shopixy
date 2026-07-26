namespace PointOfSale.Tests;

public sealed class CouponProjectionConsumerConsistencyTests
{
    [Fact]
    public void CouponProjectionConsumer_MustProjectFromSqlAuthority_NotEventPayload()
    {
        var source = ReadInfrastructureFile("Consumers", "CouponProjectionConsumer.cs");

        Assert.Contains("ApplicationDbContext dbContext", source, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCouponAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("MapSqlAuthorityToDocument(sqlCoupon, @event)", source, StringComparison.Ordinal);
        Assert.Contains("Coupon projection found no SQL authority; writing an inactive tombstone", source, StringComparison.Ordinal);
        Assert.Contains("BuildTombstoneFromSqlAuthorityAsync", source, StringComparison.Ordinal);

        Assert.DoesNotContain("var document = MapToDocument(@event)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscountValue = @event.DiscountValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicableProductIds = @event.ApplicableProductIds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExcludedProductIds = @event.ExcludedProductIds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponUsageProjection_MustDeriveUsageFromCouponUsagesTable()
    {
        var source = ReadInfrastructureFile("Consumers", "CouponProjectionConsumer.cs");

        Assert.Contains("_dbContext.CouponUsages", source, StringComparison.Ordinal);
        Assert.Contains("usage.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("usage.CouponId == couponId", source, StringComparison.Ordinal);
        Assert.Contains("!usage.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains(".Set(x => x.CurrentUsage, currentUsage)", source, StringComparison.Ordinal);

        Assert.DoesNotContain(".Set(x => x.CurrentUsage, @event.CurrentUsage)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(x => x.CurrentUsage, @event.NewTotalUsageCount)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CouponProjectionConsumer_MustPreserveSqlScopeLists()
    {
        var source = ReadInfrastructureFile("Consumers", "CouponProjectionConsumer.cs");

        Assert.Contains("ApplicableProductIds = ParseGuidList(coupon.ApplicableProductIds)", source, StringComparison.Ordinal);
        Assert.Contains("ApplicableCategoryIds = ParseGuidList(coupon.ApplicableCategoryIds)", source, StringComparison.Ordinal);
        Assert.Contains("ExcludedProductIds = ParseGuidList(coupon.ExcludedProductIds)", source, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Deserialize<List<Guid>>(json)", source, StringComparison.Ordinal);
    }

    private static string ReadInfrastructureFile(params string[] segments)
    {
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure"
        };

        pathParts.AddRange(segments);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts.ToArray())));
    }
}
