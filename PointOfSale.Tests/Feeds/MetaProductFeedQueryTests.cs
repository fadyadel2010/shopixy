using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1A: Feed projection contract against EF InMemory — eligibility matrix,
/// availability mapping, simple/variant identity, pricing authority, media
/// handling, GTIN/brand rules, deterministic order, and set-based evidence.
/// </summary>
public sealed class MetaProductFeedQueryTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private MetaProductFeedService _service = null!;
    private Tenant _tenant = null!;
    private Warehouse _warehouse = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Feed Store",
            Slug = "feedstore",
            Currency = "USD",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = "Main",
            IsActive = true
        };
        _db.Warehouses.Add(_warehouse);

        var urlResolver = new StorefrontUrlResolver(
            new ConfigurationBuilder().Build(),
            NullLogger<StorefrontUrlResolver>.Instance,
            null!);

        _service = new MetaProductFeedService(
            _db,
            new MetaFeedXmlSerializer(),
            urlResolver,
            NullLogger<MetaProductFeedService>.Instance,
            new InMemoryMetaFeedCache(),
            FeedTestFactory.CreateTelemetry());

        return _db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // =========================================================================
    // Seeding helpers
    // =========================================================================

    private Product AddEligibleProduct(
        string name,
        decimal? unitPrice = 100m,
        bool withImage = true,
        Action<Product>? customize = null,
        Guid? id = null)
    {
        var product = new Product
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Description = $"{name} description",
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = true
        };
        customize?.Invoke(product);
        _db.Products.Add(product);

        if (unitPrice.HasValue)
        {
            AddDefaultUnit(product.Id, unitPrice.Value);
        }
        if (withImage)
        {
            AddImage(product.Id, $"https://cdn.example.com/{product.Id}.jpg", position: 0);
        }

        return product;
    }

    private void AddDefaultUnit(Guid productId, decimal sellingPrice)
    {
        // Fully qualified: PointOfSale.Tests.Unit namespace shadows the entity name.
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = "Piece"
        };
        _db.Units.Add(unit);

        _db.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unit.Id,
            SellingPrice = sellingPrice,
            IsDefault = true,
            IsBaseUnit = true,
            Unit = unit
        });
    }

    private void AddImage(Guid productId, string url, int position, Guid? id = null)
    {
        _db.ProductMedia.Add(new ProductMedia
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = _tenant.Id,
            ProductId = productId,
            MediaType = "image",
            Url = url,
            Position = position,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private ProductVariant AddVariant(
        Guid productId,
        decimal price,
        decimal? onlinePrice = null,
        Action<ProductVariant>? customize = null,
        Guid? id = null)
    {
        var variant = new ProductVariant
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = _tenant.Id,
            ProductId = productId,
            Price = price,
            OnlinePrice = onlinePrice,
            IsActive = true
        };
        customize?.Invoke(variant);
        _db.ProductVariants.Add(variant);
        return variant;
    }

    private void AddStock(Guid variantId, decimal quantity, decimal reserved = 0m)
    {
        _db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            VariantId = variantId,
            WarehouseId = _warehouse.Id,
            Quantity = quantity,
            ReservedQuantity = reserved,
            Warehouse = _warehouse
        });
    }

    private Category AddCategory(string name, Guid? parentId = null, bool isDeleted = false)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = name,
            ParentCategoryId = parentId,
            IsDeleted = isDeleted
        };
        _db.Categories.Add(category);
        return category;
    }

    private void AddProductPromotion(
        Guid productId,
        decimal discountValue,
        DiscountType type = DiscountType.Percentage,
        DateTime? start = null,
        DateTime? end = null)
    {
        _db.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            Name = "Feed promo",
            DiscountType = type,
            DiscountValue = discountValue,
            Scope = PromotionScope.Product,
            AppliesToId = productId,
            StartDate = start ?? DateTime.UtcNow.AddDays(-1),
            EndDate = end ?? DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
    }

    private async Task<(IReadOnlyList<FeedProduct> Items, int Excluded)> ProjectAsync()
    {
        await _db.SaveChangesAsync();
        return await _service.ProjectFeedProductsAsync(_tenant);
    }

    // =========================================================================
    // Eligibility matrix
    // =========================================================================

    [Fact]
    public async Task EligibilityMatrix_OnlyPublishedActiveVisibleProductsAppear()
    {
        var included = AddEligibleProduct("Included Product");
        AddEligibleProduct("Draft Product", customize: p => p.PublishStatus = PublishStatus.Draft);
        AddEligibleProduct("Hidden Product", customize: p => p.IsOnlineVisible = false);
        AddEligibleProduct("Inactive Product", customize: p => p.IsActive = false);
        AddEligibleProduct("Deleted Product", customize: p => p.IsDeleted = true);
        AddEligibleProduct("Zero Price Product", unitPrice: 0m);

        var (items, excluded) = await ProjectAsync();

        Assert.Single(items);
        Assert.Equal(included.Id, items[0].ProductId);
        // Draft/hidden/inactive/deleted are folded out by the eligibility SQL
        // predicate; the composition counter tracks only per-item exclusions
        // (here: the zero-price product).
        Assert.Equal(1, excluded);
    }

    [Fact]
    public async Task BrokenCategoryLineage_ExcludesProduct_ButUncategorizedSurvives()
    {
        var deletedCategory = AddCategory("Deleted", isDeleted: true);
        AddEligibleProduct("Broken Category", customize: p => p.CategoryId = deletedCategory.Id);

        // Cycle: parentA -> parentB -> parentA.
        var parentA = AddCategory("Cycle A");
        var parentB = AddCategory("Cycle B", parentA.Id);
        parentA.ParentCategoryId = parentB.Id;
        AddEligibleProduct("Cycled Category", customize: p => p.CategoryId = parentA.Id);

        var healthyRoot = AddCategory("Shoes");
        var healthyLeaf = AddCategory("Sneakers", healthyRoot.Id);
        var categorized = AddEligibleProduct("Categorized", customize: p => p.CategoryId = healthyLeaf.Id);

        var uncategorized = AddEligibleProduct("Uncategorized");

        var (items, _) = await ProjectAsync();

        Assert.Equal(2, items.Count);
        var categorizedItem = Assert.Single(items, item => item.ProductId == categorized.Id);
        Assert.Equal("Sneakers", categorizedItem.CategoryName); // leaf name wins
        Assert.Contains(items, item => item.ProductId == uncategorized.Id && item.CategoryName == null);
    }

    // =========================================================================
    // Availability mapping — zero-stock items are retained
    // =========================================================================

    [Fact]
    public async Task Availability_MapsFromStockTrackingSemantics_AndRetainsZeroStock()
    {
        var untracked = AddEligibleProduct("Untracked", customize: p => p.TrackStock = false);

        var variantProduct = AddEligibleProduct("Variant Avail", unitPrice: null, customize: p => p.HasVariants = true);
        var inStock = AddVariant(variantProduct.Id, price: 50m);
        AddStock(inStock.Id, quantity: 5m);
        var backorder = AddVariant(variantProduct.Id, price: 60m);
        AddStock(backorder.Id, quantity: 0m);
        var outOfStock = AddVariant(variantProduct.Id, price: 70m);
        AddStock(outOfStock.Id, quantity: 0m);
        var reservedBeyondQuantity = AddVariant(variantProduct.Id, price: 80m);
        AddStock(reservedBeyondQuantity.Id, quantity: 3m, reserved: 10m);
        variantProduct.AllowBackorder = false;

        var backorderProduct = AddEligibleProduct(
            "Backorder",
            unitPrice: null,
            customize: p =>
            {
                p.HasVariants = true;
                p.AllowBackorder = true;
            });
        var backorderVariant = AddVariant(backorderProduct.Id, price: 90m);
        AddStock(backorderVariant.Id, quantity: 0m);

        var untrackedVariantProduct = AddEligibleProduct(
            "Untracked Variant",
            unitPrice: null,
            customize: p =>
            {
                p.HasVariants = true;
                p.TrackStock = false;
            });
        var untrackedVariant = AddVariant(untrackedVariantProduct.Id, price: 95m);
        AddStock(untrackedVariant.Id, quantity: 0m);

        var (items, _) = await ProjectAsync();

        Assert.Equal(FeedAvailability.InStock, ItemFor(items, untracked.Id).Availability);
        Assert.Equal(FeedAvailability.InStock, ItemFor(items, inStock.Id).Availability);
        Assert.Equal(FeedAvailability.OutOfStock, ItemFor(items, outOfStock.Id).Availability);
        // Reserved > quantity clamps to zero stock, not negative.
        Assert.Equal(FeedAvailability.OutOfStock, ItemFor(items, reservedBeyondQuantity.Id).Availability);
        Assert.Equal(FeedAvailability.AvailableForOrder, ItemFor(items, backorderVariant.Id).Availability);
        Assert.Equal(FeedAvailability.InStock, ItemFor(items, untrackedVariant.Id).Availability);
        // Zero-stock variants are retained — never excluded for stock reasons.
        Assert.Equal(4, items.Count(item => item.ProductId == variantProduct.Id));
    }

    [Fact]
    public async Task SimpleProductAvailability_MatchesStorefrontCheckoutStockPolicy()
    {
        var untrackedZero = AddEligibleProduct("Simple Untracked Zero", customize: p =>
        {
            p.TrackStock = false;
            p.AllowBackorder = false;
        });

        var trackedInStock = AddEligibleProduct("Simple Tracked In Stock");
        var trackedInStockVariant = AddVariant(trackedInStock.Id, price: 10m);
        AddStock(trackedInStockVariant.Id, quantity: 3m);

        var trackedOutOfStock = AddEligibleProduct("Simple Tracked Out");

        var trackedBackorder = AddEligibleProduct("Simple Tracked Backorder", customize: p =>
        {
            p.TrackStock = true;
            p.AllowBackorder = true;
        });

        var (items, _) = await ProjectAsync();

        Assert.Equal(FeedAvailability.InStock, ItemFor(items, untrackedZero.Id).Availability);
        Assert.Equal(FeedAvailability.InStock, ItemFor(items, trackedInStock.Id).Availability);
        Assert.Equal(FeedAvailability.OutOfStock, ItemFor(items, trackedOutOfStock.Id).Availability);
        Assert.Equal(FeedAvailability.AvailableForOrder, ItemFor(items, trackedBackorder.Id).Availability);
    }

    // =========================================================================
    // Identity contract: simple vs variant items
    // =========================================================================

    [Fact]
    public async Task SimpleProduct_EmitsOneItem_WithProductIdAndNoGroup()
    {
        var product = AddEligibleProduct("Simple Item", unitPrice: 42m);

        var (items, _) = await ProjectAsync();

        var item = Assert.Single(items);
        Assert.Equal(product.Id, item.FeedItemId);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Null(item.VariantId);
        Assert.Null(item.ItemGroupId);
        Assert.Equal(42m, item.Price);
        Assert.Equal("USD", item.Currency);
        Assert.Equal("https://store.example.com/products/simple-item", item.ProductUrl);
    }

    [Fact]
    public async Task VariantProduct_EmitsOneItemPerAdmittedVariant_WithGroupId()
    {
        var product = AddEligibleProduct("Variant Item", unitPrice: null, customize: p => p.HasVariants = true);
        var admitted1 = AddVariant(product.Id, price: 10m);
        var admitted2 = AddVariant(product.Id, price: 20m);
        AddVariant(product.Id, price: 0m);            // zero price -> not admitted
        AddVariant(product.Id, price: 30m, customize: v => v.IsActive = false); // inactive
        AddVariant(product.Id, price: 40m, customize: v =>
            v.SemanticSupersededAtUtc = DateTime.UtcNow); // superseded

        var (items, _) = await ProjectAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(product.Id, item.ItemGroupId);
            Assert.Equal(item.VariantId, item.FeedItemId);
            Assert.Equal("https://store.example.com/products/variant-item", item.ProductUrl);
        });
        Assert.Contains(items, item => item.FeedItemId == admitted1.Id);
        Assert.Contains(items, item => item.FeedItemId == admitted2.Id);
    }

    [Fact]
    public async Task VariantProduct_WithZeroAdmittedVariants_IsExcludedEntirely()
    {
        AddEligibleProduct("No Admitted Variants", unitPrice: null, customize: p =>
        {
            p.HasVariants = true;
        });
        // No variants seeded at all.

        var (items, excluded) = await ProjectAsync();

        Assert.Empty(items);
        Assert.Equal(1, excluded);
    }

    // =========================================================================
    // Pricing authority
    // =========================================================================

    [Fact]
    public async Task VariantPricing_PrefersPositiveOnlinePrice_AndUsesSkuBarcodeGtin()
    {
        var product = AddEligibleProduct("Pricing", unitPrice: null, customize: p => p.HasVariants = true);
        var withOnline = AddVariant(product.Id, price: 100m, onlinePrice: 80m, customize: v =>
        {
            v.SKU = "VAR-1";
            v.Barcode = "1234567890123";
        });
        var withoutOnline = AddVariant(product.Id, price: 60m, onlinePrice: 0m, customize: v => v.SKU = "VAR-2");

        var (items, _) = await ProjectAsync();

        var onlineItem = ItemFor(items, withOnline.Id);
        Assert.Equal(80m, onlineItem.Price);
        Assert.Equal("VAR-1", onlineItem.Sku);
        Assert.Equal("1234567890123", onlineItem.Gtin);

        var fallbackItem = ItemFor(items, withoutOnline.Id);
        Assert.Equal(60m, fallbackItem.Price);
        Assert.Equal("VAR-2", fallbackItem.Sku);
    }

    [Fact]
    public async Task SalePrice_EmittedOnlyWhenPromotionIsStrictlyLower()
    {
        var promoted = AddEligibleProduct("Promoted", unitPrice: 100m);
        AddProductPromotion(promoted.Id, discountValue: 10m); // 10% -> 90

        // Promotion outside its active window -> no winner -> no sale price.
        var unchanged = AddEligibleProduct("Unchanged", unitPrice: 100m);
        AddProductPromotion(
            unchanged.Id,
            discountValue: 10m,
            start: DateTime.UtcNow.AddDays(1),
            end: DateTime.UtcNow.AddDays(2));

        var (items, _) = await ProjectAsync();

        Assert.Equal(90m, ItemFor(items, promoted.Id).SalePrice);
        Assert.Null(ItemFor(items, unchanged.Id).SalePrice);
    }

    // =========================================================================
    // Media handling
    // =========================================================================

    [Fact]
    public async Task Media_PrimaryFollowsPositionOrder_AndInvalidUrlsAreRejected()
    {
        var product = AddEligibleProduct("Media Order", withImage: false);
        AddImage(product.Id, "https://cdn.example.com/second.jpg", position: 2);
        AddImage(product.Id, "not-a-url", position: 0);
        AddImage(product.Id, "ftp://cdn.example.com/invalid.jpg", position: 1);
        AddImage(product.Id, "http://127.0.0.1/private.jpg", position: 1);
        AddImage(product.Id, "https://cdn.example.com/signed.jpg?X-Amz-Expires=300&X-Amz-Signature=abc", position: 1);
        AddImage(product.Id, "https://cdn.example.com/manual.pdf", position: 1);
        AddImage(product.Id, "https://cdn.example.com/first.jpg", position: 1);

        var (items, _) = await ProjectAsync();

        var item = Assert.Single(items);
        Assert.Equal("https://cdn.example.com/first.jpg", item.PrimaryImageUrl);
        Assert.Equal(new[] { "https://cdn.example.com/second.jpg" }, item.AdditionalImageUrls);
    }

    [Fact]
    public async Task LocalhostStorefrontUrl_ExcludesItemsFromProjection()
    {
        _tenant.StorefrontBaseUrl = "http://shopixy.localhost:5174";
        AddEligibleProduct("Localhost Feed Product");

        var (items, excluded) = await ProjectAsync();

        Assert.Empty(items);
        Assert.Equal(1, excluded);
    }

    [Fact]
    public async Task InvalidProductSlug_ExcludesItem()
    {
        AddEligibleProduct("Bad Slug", customize: product => product.Slug = "bad slug");

        var (items, excluded) = await ProjectAsync();

        Assert.Empty(items);
        Assert.Equal(1, excluded);
    }

    [Fact]
    public async Task ProductWithoutAnyValidImage_IsExcluded()
    {
        AddEligibleProduct("No Image", withImage: false);

        var (items, excluded) = await ProjectAsync();

        Assert.Empty(items);
        Assert.Equal(1, excluded);
    }

    [Fact]
    public async Task VariantMediaId_OverridesGalleryPrimary()
    {
        var product = AddEligibleProduct("Variant Media", unitPrice: null, withImage: false, customize: p => p.HasVariants = true);
        var variantImageId = Guid.NewGuid();
        AddImage(product.Id, "https://cdn.example.com/gallery-1.jpg", position: 0);
        AddImage(product.Id, "https://cdn.example.com/variant-1.jpg", position: 1, id: variantImageId);
        var variant = AddVariant(product.Id, price: 10m, customize: v => v.MediaId = variantImageId);

        var (items, _) = await ProjectAsync();

        var item = ItemFor(items, variant.Id);
        Assert.Equal("https://cdn.example.com/variant-1.jpg", item.PrimaryImageUrl);
        Assert.Equal(new[] { "https://cdn.example.com/gallery-1.jpg" }, item.AdditionalImageUrls);
    }

    // =========================================================================
    // GTIN + brand rules
    // =========================================================================

    [Theory]
    [InlineData("12345678", true)]       // 8 digits
    [InlineData("123456789012", true)]   // 12 digits
    [InlineData("1234567890123", true)]  // 13 digits
    [InlineData("12345678901234", true)] // 14 digits
    [InlineData("12345", false)]         // wrong length
    [InlineData("123456789012345", false)] // wrong length
    [InlineData("123456789012X", false)] // non-digit
    [InlineData("", false)]
    public async Task Gtin_EmittedOnlyForDigitBarcodesWithMetaLengths(string barcode, bool expected)
    {
        var product = AddEligibleProduct($"Gtin {barcode.Length} {barcode.GetHashCode()}", customize: p => p.Barcode = barcode);

        var (items, _) = await ProjectAsync();

        var item = Assert.Single(items);
        Assert.Equal(expected ? barcode : null, item.Gtin);
    }

    [Fact]
    public async Task Brand_FallsBackToManufacturer_ThenOmitted()
    {
        var branded = AddEligibleProduct("Branded", customize: p => p.Brand = "Acme");
        var fallback = AddEligibleProduct("Fallback", customize: p =>
        {
            p.Brand = "   ";
            p.Manufacturer = "Acme Mfg";
        });
        var none = AddEligibleProduct("NoBrand");

        var (items, _) = await ProjectAsync();

        Assert.Equal("Acme", ItemFor(items, branded.Id).Brand);
        Assert.Equal("Acme Mfg", ItemFor(items, fallback.Id).Brand);
        Assert.Null(ItemFor(items, none.Id).Brand);
        Assert.DoesNotContain(items, item => item.Brand == _tenant.Name);
        Assert.All(items, item => Assert.Equal("new", item.Condition));
    }

    // =========================================================================
    // Deterministic order + set-based evidence
    // =========================================================================

    [Fact]
    public async Task Items_AreOrderedByProductIdThenVariantId()
    {
        // Deterministic Guids: ordering assertions must not depend on random
        // Guid values.
        var productAId = new Guid("00000000-0000-0000-0000-000000000001");
        var productBId = new Guid("00000000-0000-0000-0000-000000000002");
        var variant1Id = new Guid("00000000-0000-0000-0000-000000000021");
        var variant2Id = new Guid("00000000-0000-0000-0000-000000000022");

        var productB = AddEligibleProduct(
            "B Product", unitPrice: null, customize: p => p.HasVariants = true, id: productBId);
        var variant1 = AddVariant(productB.Id, price: 20m, id: variant1Id);
        var variant2 = AddVariant(productB.Id, price: 10m, id: variant2Id);

        var productA = AddEligibleProduct("A Product", unitPrice: 5m, id: productAId);

        var (items, _) = await ProjectAsync();

        Assert.Equal(3, items.Count);
        Assert.Equal(productA.Id, items[0].ProductId);
        Assert.Equal(productB.Id, items[1].ProductId);
        Assert.Equal(productB.Id, items[2].ProductId);
        // Variant ids sorted ascending inside the product block.
        Assert.Equal(variant1.Id, items[1].FeedItemId);
        Assert.Equal(variant2.Id, items[2].FeedItemId);
    }

    [Fact]
    public void Projection_IsSetBased_WithBoundedQueryCount()
    {
        // Evidence that the projection never issues per-product queries:
        // public cold paths first page ordered feed-item identities, then use
        // set-based batch queries for the selected product/variant identities.
        var sourcePath = Path.GetFullPath(Path.Combine(
            TestConfiguration.GetSolutionRoot(),
            "PointOfSale.API", "Services", "Feeds", "MetaProductFeedService.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Equal(2, CountOccurrences(source, "SingleOrDefaultAsync("));
        Assert.Contains("BuildFeedCandidateQuery(tenantId)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains(".Take(batchSize)", source, StringComparison.Ordinal);

        var batchMethodStart = source.IndexOf("ProjectCandidateBatchAsync(", StringComparison.Ordinal);
        Assert.True(batchMethodStart > 0, "Bounded batch projection marker not found.");
        var compositionStart = source.IndexOf(
            "foreach (var product in products.OrderBy(product => product.Id))",
            batchMethodStart,
            StringComparison.Ordinal);
        Assert.True(compositionStart > 0, "Composition marker not found.");
        var compositionEnd = source.IndexOf("return (items, excludedCount);", compositionStart, StringComparison.Ordinal);
        Assert.True(compositionEnd > compositionStart, "Composition end marker not found.");
        var compositionSection = source[compositionStart..compositionEnd];
        Assert.DoesNotContain("await _dbContext", compositionSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListAsync", compositionSection, StringComparison.Ordinal);
    }

    private static FeedProduct ItemFor(IReadOnlyList<FeedProduct> items, Guid feedItemId) =>
        items.Single(item => item.FeedItemId == feedItemId);

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}

