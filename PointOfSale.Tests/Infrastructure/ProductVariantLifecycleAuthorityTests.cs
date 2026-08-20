using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

public class ProductVariantLifecycleAuthorityTests
{
    [Fact]
    public async Task IsPubliclyVisibleAsync_ReturnsFalse_WhenParentProductIsArchived()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Archived parent",
            CategoryId = Guid.NewGuid(),
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Archived,
            CreatedAt = DateTime.UtcNow
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "ARCH-DETACHED-1",
            Price = 10m,
            OnlinePrice = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var visible = await ProductVariantLifecycleAuthority.IsPubliclyVisibleAsync(
            db,
            tenantId,
            productId,
            variantId,
            CancellationToken.None);

        Assert.False(visible);
    }

    [Fact]
    public async Task IsPubliclyVisibleAsync_ReturnsFalse_WhenVariantHasNoLiveParentLineage()
    {
        var tenantId = Guid.NewGuid();
        var missingProductId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = missingProductId,
            SKU = "ORPHAN-1",
            Price = 10m,
            OnlinePrice = 10m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var visible = await ProductVariantLifecycleAuthority.IsPubliclyVisibleAsync(
            db,
            tenantId,
            missingProductId,
            variantId,
            CancellationToken.None);

        Assert.False(visible);
    }

    [Fact]
    public void ResolveVisiblePrice_IgnoresInactiveDefaultVariantForVariantProducts()
    {
        var product = new ProductDocument
        {
            HasVariants = true,
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = false,
                    Price = 99m,
                    OnlinePrice = 99m
                },
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = false,
                    IsActive = true,
                    Price = 25m,
                    OnlinePrice = 20m
                }
            ]
        };

        Assert.Equal(20m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void ResolveVisiblePrice_UsesDefaultUnitForSimpleProductsEvenWhenDefaultVariantHasDifferentPrice()
    {
        var product = new ProductDocument
        {
            HasVariants = false,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 25m,
                ConversionFactor = 1m,
                IsBaseUnit = true
            },
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = true,
                    Price = 100m,
                    OnlinePrice = 110m
                }
            ]
        };

        Assert.Equal(25m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void ResolveVisiblePrice_FailsClosed_WhenVariantProductHasNoActivePricedVariant()
    {
        var product = new ProductDocument
        {
            HasVariants = true,
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = false,
                    Price = 99m,
                    OnlinePrice = 99m
                }
            ]
        };

        Assert.Equal(0m, ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(product));
    }

    [Fact]
    public void Evaluate_SimpleProductUsesDefaultUnitAndIgnoresProjectedDefaultVariant()
    {
        var product = new ProductDocument
        {
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = false,
            TrackStock = true,
            StockQuantity = 4,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 25m,
                ConversionFactor = 1m,
                IsBaseUnit = true
            },
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsDefault = true,
                    IsActive = true,
                    Price = 100m,
                    OnlinePrice = 110m,
                    TotalStock = 0m
                }
            ]
        };

        var result = ProductStorefrontVisibilityAuthority.Evaluate(product);

        Assert.True(result.IsAvailable);
        Assert.True(result.CanPurchase);
        Assert.Equal(25m, result.Price);
        Assert.True(result.InStock);
        Assert.Equal(4, result.StockQuantity);
        Assert.Null(result.UnavailableReason);
    }

    [Fact]
    public void Evaluate_SimpleProductWithoutPositiveDefaultUnitFailsClosedDespiteLegacyPrices()
    {
        var product = new ProductDocument
        {
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = false,
            Price = 99m,
            OnlinePrice = 89m,
            StockQuantity = 10,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 0m,
                ConversionFactor = 1m,
                IsBaseUnit = true
            }
        };

        var result = ProductStorefrontVisibilityAuthority.Evaluate(product);

        Assert.False(result.IsAvailable);
        Assert.False(result.CanPurchase);
        Assert.Equal(0m, result.Price);
        Assert.Equal(StorefrontPurchasabilityUnavailableReason.InvalidDefaultUnitPrice, result.UnavailableReason);
    }

    [Fact]
    public void Evaluate_VariantProductRequiresVariantSelectionButSelectedVariantCanPurchase()
    {
        var variant = new ProductVariantDocument
        {
            VariantId = Guid.NewGuid(),
            IsDefault = true,
            IsActive = true,
            Price = 30m,
            OnlinePrice = 20m,
            TotalStock = 5m
        };
        var product = new ProductDocument
        {
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = true,
            Variants = [variant]
        };

        var productResult = ProductStorefrontVisibilityAuthority.Evaluate(product);
        var variantResult = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant);

        Assert.True(productResult.IsAvailable);
        Assert.False(productResult.CanPurchase);
        Assert.True(productResult.RequiresVariantSelection);
        Assert.Equal(StorefrontPurchasabilityUnavailableReason.VariantSelectionRequired, productResult.UnavailableReason);
        Assert.True(variantResult.CanPurchase);
        Assert.Equal(20m, variantResult.Price);
    }

    [Theory]
    [InlineData(false, 0, false, true)]
    [InlineData(false, 5, false, true)]
    [InlineData(true, 0, false, false)]
    [InlineData(true, 5, false, true)]
    [InlineData(true, 0, true, true)]
    [InlineData(true, 5, true, true)]
    public void Evaluate_SimpleProductPurchasability_MatchesCheckoutStockPolicy(
        bool trackStock,
        int stockQuantity,
        bool allowBackorder,
        bool expectedCanPurchase)
    {
        var product = BuildStorefrontProduct(
            hasVariants: false,
            trackStock,
            allowBackorder,
            stockQuantity);

        var result = ProductStorefrontVisibilityAuthority.Evaluate(product);

        Assert.Equal(expectedCanPurchase, result.CanPurchase);
        Assert.Equal(expectedCanPurchase, result.InStock);
    }

    [Theory]
    [InlineData(false, 0, false, true)]
    [InlineData(false, 5, false, true)]
    [InlineData(true, 0, false, false)]
    [InlineData(true, 5, false, true)]
    [InlineData(true, 0, true, true)]
    [InlineData(true, 5, true, true)]
    public void EvaluateVariant_Purchasability_MatchesCheckoutStockPolicy(
        bool trackStock,
        decimal totalStock,
        bool allowBackorder,
        bool expectedCanPurchase)
    {
        var variant = new ProductVariantDocument
        {
            VariantId = Guid.NewGuid(),
            IsActive = true,
            IsDefault = true,
            Price = 30m,
            OnlinePrice = 20m,
            TotalStock = totalStock
        };
        var product = BuildStorefrontProduct(
            hasVariants: true,
            trackStock,
            allowBackorder,
            stockQuantity: decimal.ToInt32(totalStock),
            variants: [variant]);

        var productResult = ProductStorefrontVisibilityAuthority.Evaluate(product);
        var variantResult = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant);

        Assert.True(productResult.RequiresVariantSelection);
        Assert.Equal(expectedCanPurchase, productResult.InStock);
        Assert.Equal(expectedCanPurchase, variantResult.CanPurchase);
        Assert.Equal(expectedCanPurchase, variantResult.InStock);
    }

    [Fact]
    public async Task CommerceHubOverlay_ReturnsMissing_WhenReplayTargetsSupersededListingVariant()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 22, 10, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDbContext();
        var listing = AddCommerceHubListingAuthority(
            db,
            tenantId,
            productId,
            variantId,
            categoryId,
            now,
            ProductVariantSemanticLifecycleState.Merged);
        await db.SaveChangesAsync();

        var projection = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new CommerceHubProductDocument { Id = listing.Id, TenantId = tenantId },
            tenantId,
            CancellationToken.None);

        Assert.Null(projection);
    }

    [Fact]
    public async Task CommerceHubOverlay_IgnoresStaleInventoryFromInactiveWarehouseDuringReplay()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 22, 10, 30, 0, DateTimeKind.Utc);
        await using var db = CreateDbContext();
        var listing = AddCommerceHubListingAuthority(
            db,
            tenantId,
            productId,
            variantId,
            categoryId,
            now,
            ProductVariantSemanticLifecycleState.Current);
        db.AddRange(
            new Warehouse
            {
                Id = warehouseId,
                TenantId = tenantId,
                Name = "Detached feed warehouse",
                IsActive = false,
                CreatedAt = now
            },
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VariantId = variantId,
                WarehouseId = warehouseId,
                Quantity = 25m,
                ReservedQuantity = 0m,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var projection = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new CommerceHubProductDocument { Id = listing.Id, TenantId = tenantId },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(projection);
        Assert.Equal(0m, projection!.AvailableStock);
        Assert.False(projection.InStock);
    }

    [Fact]
    public async Task CommerceHubOverlay_ClearsStaleDerivativeAndLegacyImage_WhenSqlMediaIsMissing()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 25, 11, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDbContext();
        var listing = AddCommerceHubListingAuthority(
            db,
            tenantId,
            productId,
            variantId,
            categoryId,
            now,
            ProductVariantSemanticLifecycleState.Current);
        var product = db.Products.Local.Single(item => item.Id == productId);
        product.Images = "https://cdn.example.test/stale-product-image.jpg";
        product.Variants.Single(item => item.Id == variantId).ImageUrl =
            "https://cdn.example.test/stale-variant-image.jpg";
        await db.SaveChangesAsync();

        var projection = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new CommerceHubProductDocument
            {
                Id = listing.Id,
                TenantId = tenantId,
                Image = "https://cdn.example.test/stale-mongo-image.jpg"
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(projection);
        Assert.Null(projection!.Image);
    }

    [Fact]
    public async Task CommerceHubOverlay_RecomposesApprovedReviewAggregate_FromSqlDuringReplay()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 25, 11, 30, 0, DateTimeKind.Utc);
        await using var db = CreateDbContext();
        var listing = AddCommerceHubListingAuthority(
            db,
            tenantId,
            productId,
            variantId,
            categoryId,
            now,
            ProductVariantSemanticLifecycleState.Current);

        var approvedFour = ProductReview.Create(productId, Guid.NewGuid(), 4, null, "Good", true);
        var approvedFive = ProductReview.Create(productId, Guid.NewGuid(), 5, null, "Excellent", true);
        var rejectedOne = ProductReview.Create(productId, Guid.NewGuid(), 1, null, "Rejected", true);
        approvedFour.Approve();
        approvedFive.Approve();
        rejectedOne.Reject();
        db.ProductReviews.AddRange(approvedFour, approvedFive, rejectedOne);
        await db.SaveChangesAsync();

        var projection = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new CommerceHubProductDocument
            {
                Id = listing.Id,
                TenantId = tenantId,
                AverageRating = 1m,
                ReviewCount = 99
            },
            tenantId,
            CancellationToken.None);

        Assert.NotNull(projection);
        Assert.Equal(4.5m, projection!.AverageRating);
        Assert.Equal(2, projection.ReviewCount);
    }

    private static CommerceHubListing AddCommerceHubListingAuthority(
        ApplicationDbContext db,
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid categoryId,
        DateTime now,
        ProductVariantSemanticLifecycleState semanticLifecycleState)
    {
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            categoryId,
            commerceHubPrice: null);

        db.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = "Export replay tenant",
                Currency = "EGP",
                IsActive = true,
                Status = TenantStatus.Active
            },
            new UserGovernanceProfile
            {
                Id = Guid.NewGuid(),
                SubjectId = tenantId,
                Role = UserGovernanceRole.Vendor,
                Status = UserGovernanceStatus.Active,
                IsKycVerified = true,
                KycVerifiedAtUtc = now,
                IsMarketplaceApproved = true,
                MarketplaceApprovedAtUtc = now,
                CreatedAtUtc = now
            },
            new CommerceHubCategory
            {
                Id = categoryId,
                Name = "Global replay category",
                Slug = "global-replay-category",
                IsActive = true,
                CreatedAtUtc = now
            },
            new Product
            {
                Id = productId,
                TenantId = tenantId,
                CategoryId = Guid.NewGuid(),
                Name = "SQL visible product",
                SalePrice = 15m,
                HasVariants = true,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = now,
                Variants =
                [
                    new ProductVariant
                    {
                        Id = variantId,
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 15m,
                        OnlinePrice = 15m,
                        IsActive = true,
                        IsDefault = true,
                        SemanticLifecycleState = semanticLifecycleState,
                        SemanticSupersededAtUtc = semanticLifecycleState == ProductVariantSemanticLifecycleState.Current
                            ? null
                            : now,
                        SemanticOperationId = semanticLifecycleState == ProductVariantSemanticLifecycleState.Current
                            ? null
                            : Guid.NewGuid(),
                        CreatedAt = now
                    }
                ]
            },
            listing);

        return listing;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ProductDocument BuildStorefrontProduct(
        bool hasVariants,
        bool trackStock,
        bool allowBackorder,
        int stockQuantity,
        List<ProductVariantDocument>? variants = null) =>
        new()
        {
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = hasVariants,
            TrackStock = trackStock,
            AllowBackorder = allowBackorder,
            StockQuantity = stockQuantity,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = Guid.NewGuid(),
                Name = "Piece",
                SalePrice = 25m,
                ConversionFactor = 1m,
                IsBaseUnit = true
            },
            Variants = variants ?? []
        };
}
