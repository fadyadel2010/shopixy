using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AffiliateSqlAuthorityOverlayStockPolicyTests
{
    [Fact]
    public async Task ApplyAsync_FreshDocumentUsesEnabledSqlAffiliatePolicy()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var affiliateCategory = AffiliateCategory.CreateMainCategory("Home", $"home-{Guid.NewGuid():N}");
        affiliateCategory.Id = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedVisibleProduct(dbContext, tenantId, productId, categoryId, trackStock: true, allowBackorder: false);
        dbContext.AffiliateCategories.Add(affiliateCategory);
        var policy = AffiliateProductPolicy.Create(
            tenantId,
            productId,
            commissionRate: 0.25m,
            categoryId: affiliateCategory.Id);
        dbContext.AffiliateProductPolicies.Add(policy);
        await dbContext.SaveChangesAsync();
        var enabledAtUtc = policy.CreatedAt;

        var result = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            dbContext,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsAffiliateEnabled);
        Assert.Equal(enabledAtUtc, result.AffiliateEnabledAtUtc);
    }

    [Fact]
    public async Task ApplyAsync_FreshDocumentClearsStaleAffiliateFlagsForDisabledSqlPolicy()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var affiliateCategory = AffiliateCategory.CreateMainCategory("Garden", $"garden-{Guid.NewGuid():N}");
        affiliateCategory.Id = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedVisibleProduct(dbContext, tenantId, productId, categoryId, trackStock: true, allowBackorder: false);
        dbContext.AffiliateCategories.Add(affiliateCategory);
        var policy = AffiliateProductPolicy.Create(
            tenantId,
            productId,
            commissionRate: 0.10m,
            categoryId: affiliateCategory.Id);
        policy.Disable();
        dbContext.AffiliateProductPolicies.Add(policy);
        await dbContext.SaveChangesAsync();

        var result = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            dbContext,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId,
                IsAffiliateEnabled = true,
                AffiliateEnabledAtUtc = DateTime.UtcNow.AddDays(-1)
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.IsAffiliateEnabled);
        Assert.Null(result.AffiliateEnabledAtUtc);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ApplyAsync_ZeroStockProductAllowedBySqlStockPolicyRemainsAffiliatePurchasable(
        bool trackStock,
        bool allowBackorder)
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedVisibleProduct(dbContext, tenantId, productId, categoryId, trackStock, allowBackorder);
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };
        dbContext.Units.Add(unit);
        dbContext.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            Unit = unit,
            ConversionFactor = 1m,
            SellingPrice = 25m,
            IsDefault = true,
            IsBaseUnit = true
        });
        await dbContext.SaveChangesAsync();

        var result = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            dbContext,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId,
                TrackStock = !trackStock,
                AllowBackorder = !allowBackorder,
                StockQuantity = 0
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(trackStock, result!.TrackStock);
        Assert.Equal(allowBackorder, result.AllowBackorder);
        Assert.True(result.TryResolveAffiliatePublicPricing(out var pricing));
        Assert.True(pricing.InStock);
        Assert.Equal(0, pricing.TotalAvailableStock);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedVisibleProduct(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid productId,
        Guid categoryId,
        bool trackStock,
        bool allowBackorder)
    {
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Affiliate authority tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Affiliate authority category",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Affiliate authority product",
            SKU = $"SKU-{productId:N}",
            CategoryId = categoryId,
            IsActive = true,
            IsOnlineVisible = true,
            IsSearchable = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = false,
            TrackStock = trackStock,
            AllowBackorder = allowBackorder,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
