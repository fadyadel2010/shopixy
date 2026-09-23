using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductSearchSqlAuthorityBatchTests
{
    [Fact]
    public async Task Detail_ByIdRejectsMismatchedWarmCandidateWithoutSql()
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "warm-id-mismatch");
        var requestedProduct = Guid.NewGuid();
        var cachedProduct = Guid.NewGuid();
        var cache = new Mock<IRedisJsonCacheService>();
        cache.Setup(c => c.GetOrCreateOwnedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductReadDto { Id = cachedProduct });
        var service = new StorefrontProductService(
            Mock.Of<IProductReadRepository>(), db, cache.Object,
            Options.Create(new RedisSettings()), NullLogger<StorefrontProductService>.Instance);

        counter.Reset();
        Assert.Null(await service.GetProductByIdAsync(tenantId, requestedProduct));
        Assert.Equal(0, counter.CommandCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_RejectsMismatchedMongoCandidateWithoutSqlOrCachePopulation(bool bySlug)
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "by-id-mismatch");
        var category = SeedCategory(db, tenantId, "Category");
        var actualProduct = SeedProduct(db, tenantId, category, "Wrong candidate");
        await db.SaveChangesAsync();
        var requestedProduct = Guid.NewGuid();

        var repository = new Mock<IProductReadRepository>();
        repository.Setup(r => r.FindByProductIdAsync(
                tenantId, requestedProduct, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductDocument { TenantId = tenantId, ProductId = actualProduct });
        repository.Setup(r => r.FindBySlugAsync(
                tenantId, "wrong-candidate", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductDocument
            {
                TenantId = Guid.NewGuid(), ProductId = actualProduct, Slug = "wrong-candidate"
            });
        var cache = new Mock<IRedisJsonCacheService>();
        cache.Setup(c => c.GetOrCreateOwnedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, TimeSpan,
                Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>, TimeSpan?, CancellationToken>(
                async (_, _, _, _, factory, _, ct) =>
                {
                    var populated = await factory(ct);
                    Assert.Null(populated.Value);
                    return populated.Value;
                });
        var service = new StorefrontProductService(repository.Object, db, cache.Object,
            Options.Create(new RedisSettings()), NullLogger<StorefrontProductService>.Instance);

        counter.Reset();
        Assert.Null(bySlug
            ? await service.GetProductBySlugAsync(tenantId, "wrong-candidate")
            : await service.GetProductByIdAsync(tenantId, requestedProduct));
        Assert.Equal(0, counter.CommandCount);
    }

    [Fact]
    public async Task HomepageCategories_EmptyCandidateList_DoesNotAcquireSqlAuthority()
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "empty-homepage");
        await db.SaveChangesAsync();

        var categories = new Mock<ICategoryReadRepository>();
        categories.Setup(c => c.GetTopLevelCategoriesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CategoryDocument>());
        var service = new StorefrontHomeService(
            Mock.Of<IProductReadRepository>(), categories.Object,
            Mock.Of<IProductSearchIndexService>(), Mock.Of<IRedisJsonCacheService>(),
            db, Options.Create(new RedisSettings()), NullLogger<StorefrontHomeService>.Instance);

        counter.Reset();
        Assert.Empty(await service.GetHomepageCategoriesAsync(tenantId, "empty-homepage"));
        Assert.Equal(0, counter.CommandCount);
    }

    [Fact]
    public async Task HomepageCategories_ReuseSqlLineageSnapshot_WithoutSecondCategoryLookup()
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "homepage");
        var otherTenant = SeedTenant(db, "other");
        var live = SeedCategory(db, tenantId, "Live");
        var deleted = SeedCategory(db, tenantId, "Deleted", deleted: true);
        var foreign = SeedCategory(db, otherTenant, "Foreign");
        await db.SaveChangesAsync();

        var categories = new Mock<ICategoryReadRepository>();
        categories.Setup(c => c.GetTopLevelCategoriesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { live, deleted, foreign, Guid.NewGuid() }
                .Select(id => new CategoryDocument { CategoryId = id, TenantId = tenantId, Name = "Candidate" })
                .ToList());
        var service = new StorefrontHomeService(
            Mock.Of<IProductReadRepository>(), categories.Object,
            Mock.Of<IProductSearchIndexService>(), Mock.Of<IRedisJsonCacheService>(),
            db, Options.Create(new RedisSettings()), NullLogger<StorefrontHomeService>.Instance);

        counter.Reset();
        var result = await service.GetHomepageCategoriesAsync(tenantId, "lineage-count");

        Assert.Equal(live, Assert.Single(result).Id);
        // Tenant authority + complete tenant category snapshot; no third
        // existence query for IDs already checked by the snapshot.
        Assert.Equal(2, counter.CommandCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_ColdFactoryUsesOneSqlOverlay_WarmHitRevalidatesAuthority(bool bySlug)
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "detail-authority");
        var categoryId = SeedCategory(db, tenantId, "Visible");
        var productId = SeedProduct(db, tenantId, categoryId, "Visible product");
        db.Products.Local.Single(p => p.Id == productId).Slug = "visible-product";
        await db.SaveChangesAsync();

        var repository = new Mock<IProductReadRepository>();
        repository.Setup(r => r.FindByProductIdAsync(tenantId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProductDocument
            {
                TenantId = tenantId, ProductId = productId,
                LastEventOccurredAtUtc = DateTime.UtcNow.AddDays(-1)
            });
        repository.Setup(r => r.FindBySlugAsync(tenantId, "visible-product", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProductDocument
            {
                TenantId = tenantId, ProductId = productId,
                LastEventOccurredAtUtc = DateTime.UtcNow
            });

        // Model the owned Redis read-through contract: first invocation runs its
        // factory; the second returns a cached candidate that still needs SQL.
        ProductReadDto? candidate = null;
        bool? cacheSkipped = null;
        DateTime? cachedEventTimestamp = null;
        var cache = new Mock<IRedisJsonCacheService>();
        cache.Setup(c => c.GetOrCreateOwnedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, TimeSpan,
                Func<CancellationToken, Task<CachePopulationResult<ProductReadDto>>>, TimeSpan?, CancellationToken>(
                async (_, _, _, _, factory, _, ct) =>
                {
                    if (candidate != null) return candidate;
                    var population = await factory(ct);
                    cacheSkipped = population.SkipCache;
                    cachedEventTimestamp = population.SourceEventOccurredAtUtc;
                    if (!population.SkipCache) candidate = population.Value;
                    return population.Value;
                });

        var service = new StorefrontProductService(
            repository.Object, db, cache.Object,
            Options.Create(new RedisSettings()),
            NullLogger<StorefrontProductService>.Instance);

        async Task<ProductReadDto?> Read() => bySlug
            ? await service.GetProductBySlugAsync(tenantId, "visible-product")
            : await service.GetProductByIdAsync(tenantId, productId);

        counter.Reset();
        var cold = await Read();
        var coldCommands = counter.CommandCount;
        Assert.NotNull(cold);
        Assert.Equal(productId, cold!.Id);
        Assert.False(cacheSkipped);
        Assert.Equal(bySlug, cachedEventTimestamp.HasValue);
        // Product + active tenant in one SQL command; category, affiliate
        // policy, units and promotions remain separately authoritative.
        Assert.Equal(5, coldCommands);

        counter.Reset();
        var warm = await Read();
        Assert.NotNull(warm);
        // A cold read performs exactly one full overlay, not two; a warm
        // candidate still performs that same SQL authority work.
        Assert.Equal(coldCommands, counter.CommandCount);
        if (!bySlug)
        {
            repository.Verify(r => r.FindByProductIdAsync(
                tenantId, productId, It.IsAny<CancellationToken>()), Times.Once);
        }

        db.Products.Local.Single(p => p.Id == productId).IsActive = false;
        await db.SaveChangesAsync();
        counter.Reset();
        Assert.Null(await Read());
        Assert.True(counter.CommandCount > 0);
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int CommandCount;

        public void Reset() => CommandCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return base.ReaderExecuting(command, eventData, result);
        }
    }

    private static (ApplicationDbContext Context, CountingInterceptor Counter, SqliteConnection Connection) CreateSqliteContext()
    {
        var counter = new CountingInterceptor();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        connection.CreateFunction<string?, string?>("btrim", value => value?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (value, characters) => value?.Trim(characters?.ToCharArray() ?? []));
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (value, pattern, replacement, flags) =>
            {
                if (value is null || pattern is null)
                {
                    return value;
                }

                var regexOptions = flags?.Contains('i') == true
                    ? System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    : System.Text.RegularExpressions.RegexOptions.None;
                return System.Text.RegularExpressions.Regex.Replace(value, pattern, replacement ?? string.Empty, regexOptions);
            });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        var context = new SqliteBatchTestDbContext(options);
        context.Database.EnsureCreated();
        return (context, counter, connection);
    }

    [Theory]
    [InlineData(TenantStatus.Suspended, true, false)]
    [InlineData(TenantStatus.Active, false, false)]
    [InlineData(TenantStatus.Active, true, true)]
    public async Task ProductOverlay_FoldsTenantAuthorityIntoProductRead_AndFailsClosed(
        TenantStatus status, bool active, bool deleted)
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var ownedDb = db;
        await using var ownedConnection = connection;
        var tenantId = SeedTenant(db, "suspended");
        var otherTenantId = SeedTenant(db, "live");
        var productId = SeedProduct(db, tenantId, SeedCategory(db, tenantId, "Hidden"), "Invisible");
        var otherId = SeedProduct(db, otherTenantId, SeedCategory(db, otherTenantId, "Shown"), "Visible");
        var tenant = db.Tenants.Local.Single(t => t.Id == tenantId);
        tenant.Status = status;
        tenant.IsActive = active;
        tenant.DeletedAtUtc = deleted ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();

        counter.Reset();
        Assert.Null(await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            db, new ProductDocument { TenantId = tenantId, ProductId = productId },
            tenantId, CancellationToken.None));
        Assert.Equal(1, counter.CommandCount);

        counter.Reset();
        var results = await ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync(
            db,
            [
                new ProductDocument { TenantId = tenantId, ProductId = productId },
                new ProductDocument { TenantId = otherTenantId, ProductId = otherId }
            ], CancellationToken.None);
        Assert.Null(results[0]);
        Assert.Equal(otherTenantId, results[1]?.TenantId);
        Assert.Equal(7, counter.CommandCount);
    }

    private sealed class SqliteBatchTestDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties()
                             .Where(property => string.Equals(
                                 property.GetColumnName(),
                                 "xmin",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Guid SeedTenant(ApplicationDbContext db, string name)
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = name,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        return tenantId;
    }

    private static Guid SeedCategory(ApplicationDbContext db, Guid tenantId, string name, Guid? parentId = null, bool deleted = false)
    {
        var id = Guid.NewGuid();
        db.Categories.Add(new Category
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            ParentCategoryId = parentId,
            IsDeleted = deleted,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedProduct(
        ApplicationDbContext db,
        Guid tenantId,
        Guid categoryId,
        string name,
        bool active = true,
        bool online = true,
        bool published = true,
        bool hasVariants = false,
        bool deleted = false)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = id,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = name,
            SKU = $"SKU-{id:N}",
            IsActive = active,
            IsOnlineVisible = online,
            IsSearchable = true,
            PublishStatus = published ? PublishStatus.Published : PublishStatus.Draft,
            HasVariants = hasVariants,
            TrackStock = true,
            AllowBackorder = false,
            IsDeleted = deleted,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedUnit(ApplicationDbContext db, Guid tenantId, Guid productId, decimal price, bool isDefault = true)
    {
        var unitId = Guid.NewGuid();
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };
        db.Units.Add(unit);
        db.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            Unit = unit,
            ConversionFactor = 1m,
            SellingPrice = price,
            PurchasePrice = price * 0.6m,
            IsDefault = isDefault,
            IsBaseUnit = true
        });
        return unitId;
    }

    private static Guid SeedVariant(ApplicationDbContext db, Guid tenantId, Guid productId, decimal price, bool isDefault = true, bool isActive = true)
    {
        var variantId = Guid.NewGuid();
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"V-{variantId:N}",
            Price = price,
            OnlinePrice = price,
            CostPrice = price * 0.5m,
            IsDefault = isDefault,
            IsActive = isActive,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow
        });
        return variantId;
    }

    private static void SeedWarehouseStock(
        ApplicationDbContext db,
        Guid tenantId,
        Guid variantId,
        decimal quantity)
    {
        var warehouseId = Guid.NewGuid();
        var warehouse = new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        db.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Warehouse = warehouse,
            Quantity = quantity,
            ReservedQuantity = 0m
        });
    }

    private static void SeedPromotion(
        ApplicationDbContext db,
        Guid tenantId,
        Guid? productId,
        decimal discountValue = 10m)
    {
        var now = DateTime.UtcNow;
        db.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Batch promo",
            Scope = PromotionScope.Product,
            AppliesToId = productId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = discountValue,
            StartDate = now.AddHours(-1),
            EndDate = now.AddHours(1),
            IsActive = true
        });
    }

    private static void AssertDocumentsEqual(ProductDocument? expected, ProductDocument? actual, string context)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected!.ProductId, actual!.ProductId);
        Assert.Equal(expected.TenantId, actual.TenantId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.CategoryId, actual.CategoryId);
        Assert.Equal(expected.CategoryName, actual.CategoryName);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.IsOnlineVisible, actual.IsOnlineVisible);
        Assert.Equal(expected.Price, actual.Price);
        Assert.Equal(expected.SKU, actual.SKU);
        Assert.Equal(expected.HasActivePromotion, actual.HasActivePromotion);
        Assert.Equal(expected.PromotionDiscountValue, actual.PromotionDiscountValue);
        Assert.Equal(expected.IsAffiliateEnabled, actual.IsAffiliateEnabled);
        Assert.Equal(expected.Units.Count, actual.Units.Count);
        Assert.Equal(expected.Variants.Count, actual.Variants.Count);
        Assert.Equal(expected.Media.Count, actual.Media.Count);
        Assert.Equal(expected.StockQuantity, actual.StockQuantity);
    }

    [Fact]
    public async Task Batch_SingleProduct_MatchesSingleOverlay()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-single");
        var categoryId = SeedCategory(db, tenantId, "Cat");
        var productId = SeedProduct(db, tenantId, categoryId, "P1");
        SeedUnit(db, tenantId, productId, 100m);
        var variantId = SeedVariant(db, tenantId, productId, 100m);
        SeedWarehouseStock(db, tenantId, variantId, 5m);
        SeedPromotion(db, tenantId, productId);
        await db.SaveChangesAsync();

        var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            db,
            new ProductDocument { TenantId = tenantId, ProductId = productId },
            tenantId,
            CancellationToken.None);

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db,
            new List<ProductDocument> { new() { TenantId = tenantId, ProductId = productId } },
            tenantId,
            CancellationToken.None);

        Assert.Single(batch);
        AssertDocumentsEqual(single, batch[0], "single");
    }

    [Fact]
    public async Task Batch_TwentyProducts_MatchesSinglePerProduct()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-20");
        var categoryId = SeedCategory(db, tenantId, "Cat");
        var productIds = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var pid = SeedProduct(db, tenantId, categoryId, $"P{i}");
            SeedUnit(db, tenantId, pid, 10m + i);
            var vid = SeedVariant(db, tenantId, pid, 10m + i);
            SeedWarehouseStock(db, tenantId, vid, i);
            productIds.Add(pid);
        }

        SeedPromotion(db, tenantId, null);
        await db.SaveChangesAsync();

        var candidates = productIds
            .Select(id => new ProductDocument { TenantId = tenantId, ProductId = id })
            .ToList();

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, candidates, tenantId, CancellationToken.None);

        Assert.Equal(20, batch.Count);
        for (var i = 0; i < productIds.Count; i++)
        {
            var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                db,
                new ProductDocument { TenantId = tenantId, ProductId = productIds[i] },
                tenantId,
                CancellationToken.None);
            AssertDocumentsEqual(single, batch[i], $"index {i}");
        }
    }

    [Fact]
    public async Task Batch_EmptyResult_ReturnsEmptyWithoutError()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-empty");
        await db.SaveChangesAsync();

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db,
            new List<ProductDocument>(),
            tenantId,
            CancellationToken.None);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task Batch_MixedCategories_OnlyLiveLineageSurvives()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-cats");
        var liveParent = SeedCategory(db, tenantId, "Parent");
        var liveChild = SeedCategory(db, tenantId, "Child", liveParent);
        var deletedCat = SeedCategory(db, tenantId, "Deleted", null, deleted: true);

        var liveProduct = SeedProduct(db, tenantId, liveChild, "Live");
        var deletedCatProduct = SeedProduct(db, tenantId, deletedCat, "DeletedCat");
        var missingCatProduct = SeedProduct(db, tenantId, Guid.NewGuid(), "MissingCat");
        var emptyCatProduct = SeedProduct(db, tenantId, Guid.Empty, "EmptyCat");
        await db.SaveChangesAsync();

        var candidates = new List<ProductDocument>
        {
            new() { TenantId = tenantId, ProductId = liveProduct },
            new() { TenantId = tenantId, ProductId = deletedCatProduct },
            new() { TenantId = tenantId, ProductId = missingCatProduct },
            new() { TenantId = tenantId, ProductId = emptyCatProduct },
        };

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, candidates, tenantId, CancellationToken.None);

        Assert.NotNull(batch[0]);
        Assert.Equal("Child", batch[0]!.CategoryName);
        Assert.Null(batch[1]);
        Assert.Null(batch[2]);
        Assert.NotNull(batch[3]);
        Assert.Null(batch[3]!.CategoryName);

        // Parity with single overlay
        for (var i = 0; i < candidates.Count; i++)
        {
            var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                db,
                new ProductDocument { TenantId = tenantId, ProductId = candidates[i].ProductId },
                tenantId,
                CancellationToken.None);
            Assert.Equal(single == null, batch[i] == null);
        }
    }

    [Fact]
    public async Task Batch_AffiliateAndVisibilitySemantics_Preserved()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-aff");
        var categoryId = SeedCategory(db, tenantId, "Cat");
        var affiliateCategory = AffiliateCategory.CreateMainCategory("Home", $"home-{Guid.NewGuid():N}");
        affiliateCategory.Id = Guid.NewGuid();
        db.AffiliateCategories.Add(affiliateCategory);

        var enabledProduct = SeedProduct(db, tenantId, categoryId, "Enabled");
        var plainProduct = SeedProduct(db, tenantId, categoryId, "Plain");
        var unpublishedProduct = SeedProduct(db, tenantId, categoryId, "Unpub", active: true, online: true, published: false);
        var deletedProduct = SeedProduct(db, tenantId, categoryId, "Deleted", deleted: true);
        db.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(tenantId, enabledProduct, 0.2m, categoryId: affiliateCategory.Id));
        await db.SaveChangesAsync();

        var candidates = new List<ProductDocument>
        {
            new() { TenantId = tenantId, ProductId = enabledProduct },
            new() { TenantId = tenantId, ProductId = plainProduct },
            new() { TenantId = tenantId, ProductId = unpublishedProduct },
            new() { TenantId = tenantId, ProductId = deletedProduct },
        };

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, candidates, tenantId, CancellationToken.None);

        Assert.True(batch[0]!.IsAffiliateEnabled);
        Assert.False(batch[1]!.IsAffiliateEnabled);
        Assert.NotNull(batch[2]);
        Assert.False(batch[2]!.IsOnlineVisible);
        Assert.Null(batch[3]);

        for (var i = 0; i < candidates.Count; i++)
        {
            var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                db,
                new ProductDocument { TenantId = tenantId, ProductId = candidates[i].ProductId },
                tenantId,
                CancellationToken.None);
            Assert.Equal(single == null, batch[i] == null);
            if (single != null)
            {
                Assert.Equal(single.IsAffiliateEnabled, batch[i]!.IsAffiliateEnabled);
                Assert.Equal(single.IsOnlineVisible, batch[i]!.IsOnlineVisible);
            }
        }
    }

    [Fact]
    public async Task Batch_CrossTenantIds_DoNotLeak()
    {
        await using var db = CreateInMemoryContext();
        var tenantA = SeedTenant(db, "tenant-a");
        var tenantB = SeedTenant(db, "tenant-b");
        var catA = SeedCategory(db, tenantA, "CatA");
        var catB = SeedCategory(db, tenantB, "CatB");
        var productA = SeedProduct(db, tenantA, catA, "A-Product");
        var productB = SeedProduct(db, tenantB, catB, "B-Product");
        var productC = Guid.NewGuid();
        await db.SaveChangesAsync();

        var candidates = new List<ProductDocument>
        {
            new() { TenantId = tenantA, ProductId = productA },
            new() { TenantId = tenantA, ProductId = productB },
            new() { TenantId = tenantA, ProductId = productC },
        };

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, candidates, tenantA, CancellationToken.None);

        Assert.NotNull(batch[0]);
        Assert.Equal(tenantA, batch[0]!.TenantId);
        Assert.Equal(productA, batch[0]!.ProductId);
        Assert.Null(batch[1]);
        Assert.Null(batch[2]);
    }

    [Fact]
    public async Task BatchByTenant_MixedTenantCandidates_StayIsolatedAndOrdered()
    {
        await using var db = CreateInMemoryContext();
        var tenantA = SeedTenant(db, "batch-mixed-a");
        var tenantB = SeedTenant(db, "batch-mixed-b");
        var productA = SeedProduct(db, tenantA, Guid.Empty, "A");
        var productB = SeedProduct(db, tenantB, Guid.Empty, "B");
        await db.SaveChangesAsync();

        var results = await ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync(
            db,
            new List<ProductDocument>
            {
                new() { TenantId = tenantB, ProductId = productB },
                new() { TenantId = Guid.Empty, ProductId = productA },
                new() { TenantId = tenantA, ProductId = productA },
                new() { TenantId = tenantA, ProductId = productB }
            },
            CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.Equal((tenantB, productB), (results[0]!.TenantId, results[0]!.ProductId));
        Assert.Null(results[1]);
        Assert.Equal((tenantA, productA), (results[2]!.TenantId, results[2]!.ProductId));
        Assert.Null(results[3]);
    }

    [Fact]
    public async Task BatchByTenant_RelationalQueryCount_DoesNotScaleWithTenantCount()
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var context = db;
        await using var sqliteConnection = connection;
        var candidates = new List<ProductDocument>();
        for (var index = 0; index < 20; index++)
        {
            var tenantId = SeedTenant(db, $"cross-tenant-{index}");
            var categoryId = SeedCategory(db, tenantId, $"Category {index}");
            var productId = SeedProduct(db, tenantId, categoryId, $"Product {index}");
            candidates.Add(new ProductDocument { TenantId = tenantId, ProductId = productId });
        }

        await db.SaveChangesAsync();

        counter.Reset();
        var oneResult = await ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync(
            db,
            candidates.Take(1).ToList(),
            CancellationToken.None);
        var oneTenantCommandCount = counter.CommandCount;

        counter.Reset();
        var allResults = await ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync(
            db,
            candidates,
            CancellationToken.None);

        Assert.Single(oneResult);
        Assert.NotNull(oneResult[0]);
        Assert.Equal(20, allResults.Count(result => result != null));
        Assert.Equal(oneTenantCommandCount, counter.CommandCount);
        Assert.Equal(7, counter.CommandCount);
    }

    [Fact]
    public async Task Batch_QueryCount_IsRelationalAndChunkBounded()
    {
        var (db, counter, connection) = CreateSqliteContext();
        await using var context = db;
        await using var sqliteConnection = connection;
        var tenantId = SeedTenant(db, "batch-scale");
        var categoryId = SeedCategory(db, tenantId, "Scale");
        var productIds = new List<Guid>();
        for (var i = 0; i < 2000; i++)
        {
            productIds.Add(SeedProduct(db, tenantId, categoryId, $"S{i}"));
        }

        await db.SaveChangesAsync();

        var counts = new Dictionary<int, int>();
        foreach (var size in new[] { 1, 20, 100, 500, 1000, 1001, 2000 })
        {
            var candidates = productIds
                .Take(size)
                .Select(id => new ProductDocument { TenantId = tenantId, ProductId = id })
                .ToList();
            counter.Reset();

            var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
                db, candidates, tenantId, CancellationToken.None);

            Assert.Equal(size, batch.Count(item => item != null));
            counts[size] = counter.CommandCount;
        }

        Assert.Equal(counts[1], counts[20]);
        Assert.Equal(counts[1], counts[100]);
        Assert.Equal(counts[1], counts[500]);
        Assert.Equal(counts[1], counts[1000]);
        Assert.Equal(counts[1001], counts[2000]);
        Assert.True(counts[1001] <= (counts[1000] * 2) - 1);
        Assert.Equal(7, counts[1000]);
        Assert.Equal(13, counts[2000]);
        Assert.True(counts[1000] < 15, $"1000-product batch issued {counts[1000]} relational commands.");
        Assert.True(counts[2000] < 30, $"2000-product batch issued {counts[2000]} relational commands.");

        var duplicateCandidates = Enumerable.Repeat(productIds[0], 1000)
            .Select(id => new ProductDocument { TenantId = tenantId, ProductId = id })
            .ToList();
        counter.Reset();
        var duplicates = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, duplicateCandidates, tenantId, CancellationToken.None);

        Assert.Equal(1000, duplicates.Count(item => item != null));
        Assert.Equal(counts[1], counter.CommandCount);
    }

    [Fact]
    public async Task Batch_SingleVsBatch_HundredProducts_Identical()
    {
        await using var db = CreateInMemoryContext();
        var tenantId = SeedTenant(db, "batch-100");
        var categoryId = SeedCategory(db, tenantId, "Cat");
        var productIds = new List<Guid>();
        for (var i = 0; i < 100; i++)
        {
            var pid = SeedProduct(
                db,
                tenantId,
                categoryId,
                $"P{i}",
                active: i % 10 != 0,
                online: i % 7 != 0);
            if (i % 3 == 0)
            {
                SeedUnit(db, tenantId, pid, 5m + i);
            }

            if (i % 2 == 0)
            {
                var vid = SeedVariant(db, tenantId, pid, 5m + i, isActive: i % 5 != 0);
                SeedWarehouseStock(db, tenantId, vid, i % 4);
            }

            if (i % 4 == 0)
            {
                SeedPromotion(db, tenantId, pid, 5m + (i % 10));
            }

            productIds.Add(pid);
        }

        await db.SaveChangesAsync();

        var candidates = productIds
            .Select(id => new ProductDocument { TenantId = tenantId, ProductId = id })
            .ToList();

        var batch = await ProductSearchSqlAuthorityOverlay.ApplyBatchAsync(
            db, candidates, tenantId, CancellationToken.None);

        Assert.Equal(100, batch.Count);
        for (var i = 0; i < productIds.Count; i += 10)
        {
            var single = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                db,
                new ProductDocument { TenantId = tenantId, ProductId = productIds[i] },
                tenantId,
                CancellationToken.None);
            Assert.Equal(single == null, batch[i] == null);
            if (single != null)
            {
                AssertDocumentsEqual(single, batch[i], $"index {i}");
            }
        }
    }
}
