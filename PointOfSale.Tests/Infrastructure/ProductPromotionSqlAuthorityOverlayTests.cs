using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductPromotionSqlAuthorityOverlayTests
{
    [Fact]
    public async Task ApplyAsync_TenantWideProductPromotion_IsIncludedInCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        await using var dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"product-promotion-overlay-{Guid.NewGuid():N}")
                .Options);

        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Promotion authority tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Promotion authority category"
        });
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Promotion authority product",
            SKU = $"PROMO-{productId:N}",
            IsActive = true,
            IsOnlineVisible = true,
            IsSearchable = true,
            PublishStatus = PublishStatus.Published
        });
        dbContext.Promotions.Add(new Promotion
        {
            Id = promotionId,
            TenantId = tenantId,
            Name = "Tenant-wide product promotion",
            Scope = PromotionScope.Product,
            AppliesToId = null,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 15m,
            StartDate = nowUtc.AddMinutes(-5),
            EndDate = nowUtc.AddHours(1),
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var result = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            dbContext,
            new ProductDocument { TenantId = tenantId, ProductId = productId },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.HasActivePromotion);
        Assert.Equal(promotionId, result.ActivePromotionId);
        Assert.Equal(15m, result.PromotionDiscountValue);
    }
}
