using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductGlobalGenerationTests
{
    [Fact]
    public async Task VariantAndStockMutations_AdvanceOneParentProductGeneration()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var product = Product(tenantId);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var createdGeneration = product.ProjectionMutationVersion;

        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "GEN-VARIANT",
            Price = 10m,
            IsActive = true,
            IsDefault = true
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        var variantGeneration = product.ProjectionMutationVersion;

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Generation warehouse",
            IsActive = true
        };
        context.Warehouses.Add(warehouse);
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variant.Id,
            WarehouseId = warehouse.Id,
            Quantity = 5m
        });
        await context.SaveChangesAsync();

        Assert.True(variantGeneration > createdGeneration);
        Assert.True(product.ProjectionMutationVersion > variantGeneration);
    }

    [Fact]
    public async Task ProductPromotionMutation_AdvancesTargetProductGeneration()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var product = Product(tenantId);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var originalGeneration = product.ProjectionMutationVersion;

        context.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Generation promotion",
            Scope = PromotionScope.Product,
            AppliesToId = product.Id,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            StartDate = DateTime.UtcNow.AddMinutes(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        await context.SaveChangesAsync();

        Assert.True(product.ProjectionMutationVersion > originalGeneration);
    }

    [Fact]
    public async Task TenantWideProductPromotion_AdvancesEveryVisibleProductGenerationWithinTenantOnly()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var first = Product(tenantId);
        var second = Product(tenantId);
        var otherTenantProduct = Product(otherTenantId);
        context.Products.AddRange(first, second, otherTenantProduct);
        await context.SaveChangesAsync();

        var firstGeneration = first.ProjectionMutationVersion;
        var secondGeneration = second.ProjectionMutationVersion;
        var otherTenantGeneration = otherTenantProduct.ProjectionMutationVersion;

        context.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Tenant-wide generation promotion",
            Scope = PromotionScope.Product,
            AppliesToId = null,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            StartDate = DateTime.UtcNow.AddMinutes(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        await context.SaveChangesAsync();

        Assert.True(first.ProjectionMutationVersion > firstGeneration);
        Assert.True(second.ProjectionMutationVersion > secondGeneration);
        Assert.Equal(otherTenantGeneration, otherTenantProduct.ProjectionMutationVersion);
    }

    [Fact]
    public async Task SearchDependentMutations_AdvanceTargetProductGeneration()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var product = Product(tenantId);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var previousGeneration = product.ProjectionMutationVersion;
        context.ProductMedia.Add(new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            MediaType = "image",
            Url = "https://example.test/product.jpg"
        });
        await context.SaveChangesAsync();
        Assert.True(product.ProjectionMutationVersion > previousGeneration);

        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc"
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        previousGeneration = product.ProjectionMutationVersion;
        context.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unit.Id,
            ConversionFactor = 1m,
            SellingPrice = 12m,
            IsBaseUnit = true,
            IsDefault = true
        });
        await context.SaveChangesAsync();
        Assert.True(product.ProjectionMutationVersion > previousGeneration);

        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "GEN-ATTRIBUTE",
            Price = 12m,
            IsActive = true,
            IsDefault = true
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        previousGeneration = product.ProjectionMutationVersion;
        context.ProductVariantAttributes.Add(new ProductVariantAttribute
        {
            Id = Guid.NewGuid(),
            VariantId = variant.Id,
            Name = "Color",
            Value = "Blue"
        });
        await context.SaveChangesAsync();
        Assert.True(product.ProjectionMutationVersion > previousGeneration);

        previousGeneration = product.ProjectionMutationVersion;
        context.AffiliateProductPolicies.Add(
            AffiliateProductPolicy.Create(tenantId, product.Id, 0.10m));
        await context.SaveChangesAsync();
        Assert.True(product.ProjectionMutationVersion > previousGeneration);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"product-global-generation-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Product Product(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "Generation product",
        SKU = $"GEN-{Guid.NewGuid():N}",
        CategoryId = Guid.NewGuid(),
        IsActive = true,
        IsOnlineVisible = true,
        IsSearchable = true,
        PublishStatus = PublishStatus.Published
    };
}
