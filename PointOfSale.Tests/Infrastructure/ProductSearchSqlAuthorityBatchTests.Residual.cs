using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed partial class ProductSearchSqlAuthorityBatchTests
{
    // Reuses the existing isolated SQLite provider/fixture. Nothing provisions,
    // connects to, or writes to the configured business database.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResidualDetail_ReplacesAllCandidateCommercialFields_WithCurrentSql(bool warm) =>
        await VerifyResidualDetailAuthorityAsync(warm);

    internal static async Task VerifyResidualDetailAuthorityAsync(bool warm)
    {
        var (db, _, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenant = SeedTenant(db, "residual-detail");
        var category = SeedCategory(db, tenant, "SQL category");
        var id = SeedProduct(db, tenant, category, "SQL name", hasVariants: true);
        var product = db.Products.Local.Single(p => p.Id == id);
        product.Slug = "sql-product";
        product.Description = "SQL narrative";
        product.DescriptionAr = "وصف من قاعدة البيانات";
        var variantId = SeedVariant(db, tenant, id, 137m);
        SeedWarehouseStock(db, tenant, variantId, 7m);
        await db.SaveChangesAsync();

        var stale = new ProductDocument { TenantId = tenant, ProductId = id, Slug = "sql-product", Name = "Projection forgery", Description = "Projection narrative", Price = 1, StockQuantity = 999 };
        var service = ResidualService(db, tenant, stale, warm);
        foreach (var bySlug in new[] { false, true })
        {
            var result = bySlug ? await service.GetProductBySlugAsync(tenant, "sql-product") : await service.GetProductByIdAsync(tenant, id);
            Assert.NotNull(result);
            Assert.Equal(id, result.Id);
            Assert.Equal("SQL name", result.Name);
            Assert.Equal("SQL narrative", result.Description);
            Assert.Equal("SQL category", result.CategoryName);
            Assert.Equal(137m, result.Pricing.CurrentPrice);
            Assert.Equal(137m, Assert.Single(result.Variants).Pricing.CurrentPrice);
            Assert.Equal(7, result.Variants[0].Availability.MaximumQuantity);
            Assert.False(result.Availability.CanPurchase); // Backend requires option selection.
            Assert.Empty(result.Media); // Never revive media from a stale envelope.
        }

        product.IsOnlineVisible = false;
        await db.SaveChangesAsync();
        Assert.Null(await service.GetProductByIdAsync(tenant, id));
        Assert.Null(await service.GetProductBySlugAsync(tenant, "sql-product"));
        Assert.Null(await service.GetProductByIdAsync(Guid.NewGuid(), id));
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("default")]
    [InlineData("invalid")]
    [InlineData("partial")]
    [InlineData("inactive")]
    [InlineData("unavailable")]
    [InlineData("ambiguous")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-product")]
    public async Task ResidualVariant_ResolvesOnlyCurrentTenantProductAndExactOptions(string scenario) =>
        await VerifyResidualVariantAuthorityAsync(scenario);

    internal static async Task VerifyResidualVariantAuthorityAsync(string scenario)
    {
        var (db, _, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenant = SeedTenant(db, "residual-variant");
        var id = SeedProduct(db, tenant, SeedCategory(db, tenant, "SQL category"), "SQL variant", hasVariants: true);
        var variantId = SeedVariant(db, tenant, id, 137m, isActive: scenario != "inactive");
        SeedWarehouseStock(db, tenant, variantId, scenario == "unavailable" ? 0m : 7m);
        AddResidualAttribute(db, variantId, "Color", "Red");
        AddResidualAttribute(db, variantId, "Size", "Large");
        if (scenario == "ambiguous")
        {
            var duplicate = SeedVariant(db, tenant, id, 999m, isDefault: false);
            AddResidualAttribute(db, duplicate, "Color", "Red");
            AddResidualAttribute(db, duplicate, "Size", "Large");
        }
        await db.SaveChangesAsync();
        var service = ResidualService(db, tenant, new ProductDocument { TenantId = tenant, ProductId = id }, warm: true);
        var options = scenario switch
        {
            "default" => new Dictionary<string, string>(),
            "invalid" => new Dictionary<string, string> { ["Color"] = "Nonexistent", ["Size"] = "Large" },
            "partial" => new Dictionary<string, string> { ["Color"] = "Red" },
            _ => new Dictionary<string, string> { ["Color"] = "Red", ["Size"] = "Large" }
        };
        var result = await service.ResolveProductVariantAsync(scenario == "foreign-tenant" ? Guid.NewGuid() : tenant,
            scenario == "foreign-product" ? Guid.NewGuid() : id, new() { SelectedOptions = options });
        if (scenario is "exact" or "default" or "unavailable")
        {
            Assert.NotNull(result);
            Assert.Equal(id, result.ProductId);
            Assert.Equal(variantId, result.ResolvedVariantId);
            Assert.Equal(variantId, result.Variant.Id);
            Assert.Equal(137m, result.Variant.Pricing.CurrentPrice);
            Assert.Equal(scenario != "unavailable", result.Variant.Availability.CanPurchase);
        }
        else Assert.Null(result);
    }

    private static void AddResidualAttribute(ApplicationDbContext db, Guid variant, string name, string value) =>
        db.ProductVariantAttributes.Add(new ProductVariantAttribute { Id = Guid.NewGuid(), VariantId = variant, Name = name, Value = value });

    private static StorefrontProductService ResidualService(ApplicationDbContext db, Guid tenant, ProductDocument candidate, bool warm)
    {
        var repository = new Mock<IProductReadRepository>();
        repository.Setup(r => r.FindByProductIdAsync(tenant, candidate.ProductId, It.IsAny<CancellationToken>())).ReturnsAsync(candidate);
        repository.Setup(r => r.FindBySlugAsync(tenant, "sql-product", true, It.IsAny<CancellationToken>())).ReturnsAsync(candidate);
        var cache = new Mock<IRedisJsonCacheService>();
        cache.Setup(c => c.GetOrCreateOwnedWithStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, TimeSpan, Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>, TimeSpan?, CancellationToken>(
                async (_, _, _, _, factory, _, ct) => warm
                    ? new CacheReadResult<ProductReadDto>(new ProductReadDto { Id = candidate.ProductId, Name = "Cached forgery", Price = 1, StockQuantity = 999 }, Refreshed: false)
                    : new CacheReadResult<ProductReadDto>((await factory(ct)).Value, Refreshed: true));
        return new StorefrontProductService(repository.Object, db, cache.Object, Options.Create(new RedisSettings()), NullLogger<StorefrontProductService>.Instance);
    }
}
