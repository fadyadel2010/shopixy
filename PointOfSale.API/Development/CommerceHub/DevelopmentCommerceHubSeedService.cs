using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Development.CommerceHub;

public interface IDevelopmentCommerceHubSeedService
{
    Task<CommerceHubSeedResult> SeedAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubSeedResetResult> ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class DevelopmentCommerceHubSeedService : IDevelopmentCommerceHubSeedService
{
    private static readonly Guid OrbitTenantId = Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce1002");
    private static readonly Guid CasaTenantId = Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce1003");

    private static readonly Guid OrbitPosCategoryId = Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce2101");
    private static readonly Guid CasaPosCategoryId = Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce2102");

    private static readonly Guid ElectronicsCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid HomeGardenCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid TvsCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000011");
    private static readonly Guid MobilePhonesCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000012");
    private static readonly Guid FurnitureCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000031");
    private static readonly Guid KitchenCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000032");

    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ILogger<DevelopmentCommerceHubSeedService> _logger;

    public DevelopmentCommerceHubSeedService(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ILogger<DevelopmentCommerceHubSeedService> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _searchIndexService = searchIndexService;
        _logger = logger;
    }

    public async Task<CommerceHubSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await EnsureCommerceHubCategoriesExistAsync(cancellationToken);

        foreach (var tenant in BuildTenantSpecs())
        {
            await UpsertTenantAsync(tenant, now, cancellationToken);
        }

        foreach (var category in BuildPosCategorySpecs())
        {
            await UpsertPosCategoryAsync(category, now, cancellationToken);
        }

        foreach (var product in BuildProductSpecs())
        {
            await UpsertProductAsync(product, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var seededDocuments = new List<CommerceHubProductDocument>();

        foreach (var listing in BuildListingSpecs())
        {
            var document = listing.ToDocument(now);
            var searchIndexed = await TrySyncSearchAsync(document, cancellationToken);

            await _mongoDbContext.CommerceHubProducts.ReplaceOneAsync(
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, document.Id),
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            seededDocuments.Add(document);

            _logger.LogInformation(
                "Development CommerceHub seed upserted listing | ListingId: {ListingId} | ProductName: {ProductName} | SearchIndexed: {SearchIndexed}",
                document.Id,
                document.ProductName,
                searchIndexed);
        }

        return new CommerceHubSeedResult(
            BuildTenantSpecs().Length,
            BuildPosCategorySpecs().Length,
            BuildProductSpecs().Length,
            seededDocuments.Count,
            seededDocuments.Select(x => x.Id).ToArray());
    }

    public async Task<CommerceHubSeedResetResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        var listingIds = BuildListingSpecs().Select(x => x.ListingId).ToArray();

        foreach (var listingId in listingIds)
        {
            await _searchIndexService.DeleteAsync(listingId, cancellationToken: cancellationToken);
        }

        var deletedMongo = await _mongoDbContext.CommerceHubProducts.DeleteManyAsync(
            Builders<CommerceHubProductDocument>.Filter.In(x => x.Id, listingIds),
            cancellationToken);

        var products = await _dbContext.Set<Product>()
            .Where(x => BuildProductSpecs().Select(p => p.ProductId).Contains(x.Id))
            .ToListAsync(cancellationToken);

        var posCategories = await _dbContext.Set<Category>()
            .Where(x => BuildPosCategorySpecs().Select(c => c.CategoryId).Contains(x.Id))
            .ToListAsync(cancellationToken);

        var tenants = await _dbContext.Set<Tenant>()
            .Where(x => BuildTenantSpecs().Select(t => t.TenantId).Contains(x.Id))
            .ToListAsync(cancellationToken);

        _dbContext.RemoveRange(products);
        _dbContext.RemoveRange(posCategories);
        _dbContext.RemoveRange(tenants);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CommerceHubSeedResetResult(
            tenants.Count,
            posCategories.Count,
            products.Count,
            deletedMongo.DeletedCount);
    }

    private async Task EnsureCommerceHubCategoriesExistAsync(CancellationToken cancellationToken)
    {
        var requiredIds = BuildRequiredCommerceHubCategoryIds();

        var existingIds = await _dbContext.CommerceHubCategories
            .AsNoTracking()
            .Where(x => requiredIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var missingIds = requiredIds.Except(existingIds).ToArray();
        if (missingIds.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "CommerceHub global categories are missing. Apply the CommerceHub category migration before running the development seed. Missing category IDs: " +
            string.Join(", ", missingIds));
    }

    private async Task<bool> TrySyncSearchAsync(CommerceHubProductDocument document, CancellationToken cancellationToken)
    {
        document.SearchLastAttemptedAtUtc = DateTime.UtcNow;

        var searchDocument = CommerceHubSearchDocumentMapper.Map(document);

        var synced = await _searchIndexService.IndexAsync(searchDocument, cancellationToken: cancellationToken);

        if (synced)
        {
            document.SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced;
            document.LastSyncedAt = DateTime.UtcNow;
            document.SearchLastSucceededAtUtc = document.LastSyncedAt;
            document.SearchLastError = null;
            return true;
        }

        document.SearchSyncStatus = CommerceHubProductDocument.SearchSyncFailed;
        document.SearchLastError = "Development seed search sync failed.";
        return false;
    }

    private async Task UpsertTenantAsync(TenantSeedSpec spec, DateTime now, CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Set<Tenant>().FirstOrDefaultAsync(x => x.Id == spec.TenantId, cancellationToken);

        if (tenant == null)
        {
            tenant = new Tenant
            {
                Id = spec.TenantId,
                CreatedAtUtc = now
            };

            _dbContext.Set<Tenant>().Add(tenant);
        }

        tenant.Name = spec.Name;
        tenant.Slug = spec.Slug;
        tenant.ContactEmail = spec.ContactEmail;
        tenant.Currency = "EGP";
        tenant.Country = "EG";
        tenant.SetLifecycleStatus(TenantStatus.Active, nowUtc: now);
        tenant.UpdatedAtUtc = now;
    }

    private async Task UpsertPosCategoryAsync(PosCategorySeedSpec spec, DateTime now, CancellationToken cancellationToken)
    {
        var category = await _dbContext.Set<Category>().FirstOrDefaultAsync(x => x.Id == spec.CategoryId, cancellationToken);

        if (category == null)
        {
            category = new Category
            {
                Id = spec.CategoryId,
                CreatedAt = now
            };

            _dbContext.Set<Category>().Add(category);
        }

        category.TenantId = spec.TenantId;
        category.Name = spec.Name;
        category.Description = spec.Description;
        category.ParentCategoryId = null;
        category.UpdatedAt = now;
        category.IsDeleted = false;
    }

    private async Task UpsertProductAsync(ProductSeedSpec spec, DateTime now, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Set<Product>()
            .Include(x => x.ProductUnits)
            .FirstOrDefaultAsync(x => x.Id == spec.ProductId, cancellationToken);

        if (product == null)
        {
            product = new Product
            {
                Id = spec.ProductId,
                CreatedAt = now
            };

            _dbContext.Set<Product>().Add(product);
        }

        product.TenantId = spec.TenantId;
        product.Name = spec.ProductName;
        product.Description = spec.Description;
        product.CategoryId = spec.PosCategoryId;
        product.PurchasePrice = 0m;
        product.SalePrice = 0m;
        product.OnlinePrice = null;
        product.PriceIncludesTax = true;
        product.TaxRate = 14m;
        product.Unit = "Piece";
        product.MinimumStockLevel = 2m;
        product.IsActive = true;
        product.TrackStock = true;
        product.HasVariants = false;
        product.PublishStatus = PublishStatus.Published;
        product.IsOnlineVisible = true;
        product.IsSearchable = true;
        product.IsFeatured = spec.IsFeatured;
        product.LowStockThreshold = 2;
        product.Brand = spec.Brand;
        product.SKU = spec.Sku;
        product.Slug = spec.Slug;
        product.UpdatedAt = now;
        product.IsDeleted = false;

        var unit = await EnsurePieceUnitAsync(spec.TenantId, cancellationToken);
        var defaultUnit = product.ProductUnits.FirstOrDefault(x => !x.IsDeleted && x.IsDefault)
            ?? product.ProductUnits.FirstOrDefault(x => !x.IsDeleted);

        if (defaultUnit == null)
        {
            defaultUnit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Product = product
            };

            product.ProductUnits.Add(defaultUnit);
        }

        defaultUnit.UnitId = unit.Id;
        defaultUnit.Unit = unit;
        defaultUnit.ConversionFactor = 1m;
        defaultUnit.PurchasePrice = spec.Price * 0.65m;
        defaultUnit.SellingPrice = spec.Price;
        defaultUnit.IsDefault = true;
        defaultUnit.IsBaseUnit = true;
        defaultUnit.IsDeleted = false;

        ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit);
    }

    private async Task<Unit> EnsurePieceUnitAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var unit = await _dbContext.Set<Unit>()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Name == "Piece", cancellationToken);

        if (unit != null)
        {
            return unit;
        }

        unit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            Description = "Piece",
            IsActive = true
        };

        _dbContext.Set<Unit>().Add(unit);
        return unit;
    }

    private static TenantSeedSpec[] BuildTenantSpecs() =>
        new[]
        {
            new TenantSeedSpec(OrbitTenantId, "Orbit Electronics", "orbit-electronics", "hello@orbit.local"),
            new TenantSeedSpec(CasaTenantId, "Casa Living", "casa-living", "hello@casa.local")
        };

    private static PosCategorySeedSpec[] BuildPosCategorySpecs() =>
        new[]
        {
            new PosCategorySeedSpec(
                OrbitPosCategoryId,
                OrbitTenantId,
                "Marketplace Catalog",
                "Tenant-owned POS category reserved for development CommerceHub products."),
            new PosCategorySeedSpec(
                CasaPosCategoryId,
                CasaTenantId,
                "Marketplace Catalog",
                "Tenant-owned POS category reserved for development CommerceHub products.")
        };

    private static Guid[] BuildRequiredCommerceHubCategoryIds() =>
        new[]
        {
            ElectronicsCategoryId,
            HomeGardenCategoryId,
            TvsCategoryId,
            MobilePhonesCategoryId,
            FurnitureCategoryId,
            KitchenCategoryId
        };

    private static ProductSeedSpec[] BuildProductSpecs() =>
        BuildListingSpecs()
            .Select(x => new ProductSeedSpec(
                x.ProductId,
                x.TenantId,
                ResolvePosCategoryId(x.TenantId),
                x.ProductName,
                x.Price,
                x.Brand,
                x.Sku,
                x.Slug,
                x.Description,
                x.IsFeatured))
            .ToArray();

    private static Guid ResolvePosCategoryId(Guid tenantId) =>
        tenantId == OrbitTenantId ? OrbitPosCategoryId : CasaPosCategoryId;

    private static ListingSeedSpec[] BuildListingSpecs() =>
        new[]
        {
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3001"),
                OrbitTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4001"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5001"),
                ElectronicsCategoryId,
                "Orbit Noise-Cancelling Headphones",
                "Orbit",
                "Premium wireless headphones with deep bass and all-day comfort.",
                "orbit-noise-cancelling-headphones",
                "ORBIT-AUDIO-01",
                4899m,
                14m,
                true),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3002"),
                OrbitTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4002"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5002"),
                ElectronicsCategoryId,
                "Orbit Portable Bluetooth Speaker",
                "Orbit",
                "Compact speaker with strong battery life and room-filling sound.",
                "orbit-portable-bluetooth-speaker",
                "ORBIT-AUDIO-02",
                2599m,
                32m,
                true),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3003"),
                OrbitTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4003"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5003"),
                TvsCategoryId,
                "Orbit 4K Streaming Stick",
                "Orbit",
                "Fast media streaming with 4K playback and voice remote support.",
                "orbit-4k-streaming-stick",
                "ORBIT-TV-01",
                1999m,
                20m,
                false),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3004"),
                OrbitTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4004"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5004"),
                MobilePhonesCategoryId,
                "Orbit Smart Watch Pro",
                "Orbit",
                "Health tracking, notifications, and a bright always-on display.",
                "orbit-smart-watch-pro",
                "ORBIT-WEAR-01",
                6299m,
                8m,
                true),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3005"),
                CasaTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4005"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5005"),
                KitchenCategoryId,
                "Casa Espresso Machine",
                "Casa",
                "Countertop espresso machine with milk frother and compact footprint.",
                "casa-espresso-machine",
                "CASA-KITCHEN-01",
                8999m,
                6m,
                true),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3006"),
                CasaTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4006"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5006"),
                KitchenCategoryId,
                "Casa Digital Air Fryer",
                "Casa",
                "Family-size air fryer with presets for everyday fast cooking.",
                "casa-digital-air-fryer",
                "CASA-KITCHEN-02",
                4599m,
                0m,
                false),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3007"),
                CasaTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4007"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5007"),
                FurnitureCategoryId,
                "Casa Linen Bed Set",
                "Casa",
                "Soft neutral bedding set designed for warm-weather comfort.",
                "casa-linen-bed-set",
                "CASA-HOME-01",
                3299m,
                18m,
                false),
            new ListingSeedSpec(
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce3008"),
                CasaTenantId,
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce4008"),
                Guid.Parse("4b4f5312-f90c-4b20-9bdb-74b7f0ce5008"),
                HomeGardenCategoryId,
                "Casa Scented Candle Collection",
                "Casa",
                "Four-piece candle set for calm evenings and gift-ready displays.",
                "casa-scented-candle-collection",
                "CASA-HOME-02",
                899m,
                25m,
                false)
        };

    private sealed record TenantSeedSpec(Guid TenantId, string Name, string Slug, string ContactEmail);

    private sealed record PosCategorySeedSpec(Guid CategoryId, Guid TenantId, string Name, string Description);

    private sealed record ProductSeedSpec(
        Guid ProductId,
        Guid TenantId,
        Guid PosCategoryId,
        string ProductName,
        decimal Price,
        string Brand,
        string Sku,
        string Slug,
        string Description,
        bool IsFeatured);

    private sealed record ListingSeedSpec(
        Guid ListingId,
        Guid TenantId,
        Guid ProductId,
        Guid VariantId,
        Guid CommerceHubCategoryId,
        string ProductName,
        string Brand,
        string Description,
        string Slug,
        string Sku,
        decimal Price,
        decimal AvailableStock,
        bool IsFeatured)
    {
        public CommerceHubProductDocument ToDocument(DateTime now)
        {
            var (categoryName, categoryPath) = CommerceHubCategoryId switch
            {
                _ when CommerceHubCategoryId == ElectronicsCategoryId => ("Electronics", "Electronics"),
                _ when CommerceHubCategoryId == HomeGardenCategoryId => ("Home & Garden", "Home & Garden"),
                _ when CommerceHubCategoryId == TvsCategoryId => ("TVs", "Electronics > TVs"),
                _ when CommerceHubCategoryId == MobilePhonesCategoryId => ("Mobile Phones", "Electronics > Mobile Phones"),
                _ when CommerceHubCategoryId == FurnitureCategoryId => ("Furniture", "Home & Garden > Furniture"),
                _ => ("Kitchen", "Home & Garden > Kitchen")
            };

            var storeName = TenantId == OrbitTenantId ? "Orbit Electronics" : "Casa Living";
            var storeShippingFee = TenantId == OrbitTenantId ? 35m : 55m;

            return new CommerceHubProductDocument
            {
                Id = ListingId,
                TenantId = TenantId,
                StoreName = storeName,
                StoreShippingFee = storeShippingFee,
                ProductId = ProductId,
                VariantId = VariantId,
                ProductName = ProductName,
                Description = $"{ProductName} curated for CommerceHub discovery.",
                Image = null,
                Price = Price,
                CommerceHubPriceOverride = null,
                PriceSource = "ProductVariantEffectiveOnlinePrice",
                OverridePresent = false,
                CategoryId = CommerceHubCategoryId,
                CategoryName = categoryName,
                CategoryPath = categoryPath,
                Currency = "EGP",
                AvailableStock = AvailableStock,
                InStock = AvailableStock > 0,
                IsPublished = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
    }
}

public sealed record CommerceHubSeedResult(
    int SeededTenants,
    int SeededCategories,
    int SeededProducts,
    int SeededListings,
    IReadOnlyCollection<Guid> ListingIds);

public sealed record CommerceHubSeedResetResult(
    int RemovedTenants,
    int RemovedCategories,
    int RemovedProducts,
    long RemovedListings);
