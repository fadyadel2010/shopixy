using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ProductSqlAuthorityProjectionWriterTests
{
    [Fact]
    public async Task ApplyAsync_RecomposesUnitsAndStorefrontMetadataFromCurrentSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var pieceUnitId = Guid.NewGuid();
        var cartonUnitId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedActiveTenantAndCategory(dbContext, tenantId, categoryId);
        var product = CreateVisibleProduct(tenantId, productId, categoryId);
        product.FeaturesJson = """["Fresh roasted","Single origin"]""";
        product.FaqJson = """[{"Question":"How many cups?","Answer":"Twelve"}]""";
        product.TagsJson = """["coffee","featured"]""";
        product.AiGenerated = true;
        product.AiMetadataJson = """{"generator":"test-suite"}""";

        var piece = CreateUnit(tenantId, pieceUnitId, "Piece", "pc");
        var carton = CreateUnit(tenantId, cartonUnitId, "Carton", "ctn");
        dbContext.Products.Add(product);
        dbContext.Units.AddRange(piece, carton);
        dbContext.ProductUnits.AddRange(
            CreateProductUnit(productId, pieceUnitId, piece, salePrice: 25m, conversionFactor: 1m, isDefault: true, isBaseUnit: true, purchasePrice: 42m),
            CreateProductUnit(productId, cartonUnitId, carton, salePrice: 280m, conversionFactor: 12m, isDefault: false, isBaseUnit: false));
        await dbContext.SaveChangesAsync();

        ProductDocument? capturedProjection = null;
        var repository = CreateRepositoryMock(captured => capturedProjection = captured);
        var mutationBoundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var cacheInvalidator = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Strict);
        cacheInvalidator
            .Setup(item => item.InvalidateProductAsync(
                tenantId,
                productId,
                It.IsAny<string?>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.NotNull(capturedProjection))
            .Returns(Task.CompletedTask);
        var writer = new ProductSqlAuthorityProjectionWriter(
            dbContext,
            mutationBoundary,
            cacheInvalidator.Object,
            NullLogger<ProductSqlAuthorityProjectionWriter>.Instance);

        var result = await writer.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    Price = 999m,
                    DefaultUnit = null,
                    Units = new List<ProductUnitDocument>()
                },
                Guid.NewGuid(),
                DateTime.UtcNow,
                EventVersion: 7));

        Assert.Equal(ProductSqlAuthorityProjectionWriteDisposition.Upserted, result.Disposition);
        Assert.NotNull(capturedProjection);
        Assert.Equal(25m, capturedProjection!.StorefrontEffectivePrice);
        Assert.Equal(pieceUnitId, capturedProjection.DefaultUnit!.UnitId);
        Assert.Equal(new[] { pieceUnitId, cartonUnitId }, capturedProjection.Units.Select(unit => unit.UnitId));
        Assert.Equal(new[] { 25m, 280m }, capturedProjection.Units.Select(unit => unit.SalePrice));
        Assert.Equal(42m, capturedProjection.PurchasePrice);
        Assert.Equal(new[] { "Fresh roasted", "Single origin" }, capturedProjection.Features);
        Assert.Equal(new[] { "coffee", "featured" }, capturedProjection.Tags);
        Assert.Single(capturedProjection.Faqs);
        Assert.Equal("How many cups?", capturedProjection.Faqs[0].Question);
        Assert.True(capturedProjection.AiGenerated);
        Assert.Equal("test-suite", capturedProjection.AiMetadata?["generator"]?.AsString);
        cacheInvalidator.VerifyAll();
    }

    [Fact]
    public async Task ApplyAsync_UsesSqlProductGenerationInsteadOfStreamLocalEventVersion()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedActiveTenantAndCategory(dbContext, tenantId, categoryId);
        var product = CreateVisibleProduct(tenantId, productId, categoryId);
        var unit = CreateUnit(tenantId, unitId, "Piece", "pc");
        dbContext.Products.Add(product);
        dbContext.Units.Add(unit);
        dbContext.ProductUnits.Add(
            CreateProductUnit(productId, unitId, unit, salePrice: 25m, conversionFactor: 1m, isDefault: true, isBaseUnit: true));
        await dbContext.SaveChangesAsync();

        ProductDocument? capturedProjection = null;
        var repository = CreateRepositoryMock(captured => capturedProjection = captured);
        var writer = new ProductSqlAuthorityProjectionWriter(
            dbContext,
            new StorefrontProductProjectionMutationBoundary(repository.Object));
        var streamLocalEventVersion = ProductEventVersionHelper.ToProductEventVersion(
            product.ProjectionMutationVersion + 100);

        var result = await writer.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId
                },
                Guid.NewGuid(),
                DateTime.UtcNow,
                streamLocalEventVersion));

        Assert.Equal(ProductSqlAuthorityProjectionWriteDisposition.Upserted, result.Disposition);
        Assert.NotNull(capturedProjection);
        Assert.Equal(product.ProjectionMutationVersion, capturedProjection!.ProductGeneration);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
            capturedProjection.LastEventVersion);
        Assert.NotEqual(streamLocalEventVersion, capturedProjection.LastEventVersion);
    }

    [Fact]
    public void ProductSearchDocumentMapper_UsesDefaultUnitPriceForSimpleProducts()
    {
        var product = new ProductDocument
        {
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Name = "Simple unit-priced product",
            HasVariants = false,
            IsActive = true,
            IsOnlineVisible = true,
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

        var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(product);

        Assert.Equal(25d, searchDocument.Price);
    }

    [Fact]
    public async Task ApplyAsync_RecomposesUnitUpdatesAndDeletesFromSqlAuthority()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var pieceUnitId = Guid.NewGuid();
        var cartonUnitId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedActiveTenantAndCategory(dbContext, tenantId, categoryId);
        var product = CreateVisibleProduct(tenantId, productId, categoryId);
        var piece = CreateUnit(tenantId, pieceUnitId, "Piece", "pc");
        var carton = CreateUnit(tenantId, cartonUnitId, "Carton", "ctn");
        var productPieceUnit = CreateProductUnit(productId, pieceUnitId, piece, salePrice: 25m, conversionFactor: 1m, isDefault: true, isBaseUnit: true);
        var productCartonUnit = CreateProductUnit(productId, cartonUnitId, carton, salePrice: 280m, conversionFactor: 12m, isDefault: false, isBaseUnit: false);

        dbContext.Products.Add(product);
        dbContext.Units.AddRange(piece, carton);
        dbContext.ProductUnits.AddRange(productPieceUnit, productCartonUnit);
        await dbContext.SaveChangesAsync();

        ProductDocument? capturedProjection = null;
        var repository = CreateRepositoryMock(captured => capturedProjection = captured);
        var mutationBoundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var writer = new ProductSqlAuthorityProjectionWriter(dbContext, mutationBoundary);

        await writer.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId
                },
                Guid.NewGuid(),
                DateTime.UtcNow,
                EventVersion: 8));

        Assert.NotNull(capturedProjection);
        Assert.Equal(pieceUnitId, capturedProjection!.DefaultUnit!.UnitId);
        Assert.Equal(2, capturedProjection.Units.Count);

        productPieceUnit.IsDeleted = true;
        productCartonUnit.IsDefault = true;
        productCartonUnit.SellingPrice = 300m;
        product.AdvanceProjectionMutationVersion(9);
        await dbContext.SaveChangesAsync();

        await writer.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId
                },
                Guid.NewGuid(),
                DateTime.UtcNow.AddMinutes(1),
                EventVersion: 9));

        Assert.NotNull(capturedProjection);
        Assert.Equal(cartonUnitId, capturedProjection!.DefaultUnit!.UnitId);
        Assert.Equal(300m, capturedProjection.DefaultUnit.SalePrice);
        Assert.Single(capturedProjection.Units);
        Assert.Equal(cartonUnitId, capturedProjection.Units[0].UnitId);
    }

    [Fact]
    public async Task ApplyAsync_RejectsSimpleProductsWithoutDefaultUnitBeforeProjectionPersistence()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedActiveTenantAndCategory(dbContext, tenantId, categoryId);
        dbContext.Products.Add(CreateVisibleProduct(tenantId, productId, categoryId));
        await dbContext.SaveChangesAsync();

        ProductDocument? capturedProjection = null;
        var repository = CreateRepositoryMock(captured => capturedProjection = captured);
        var mutationBoundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var writer = new ProductSqlAuthorityProjectionWriter(dbContext, mutationBoundary);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.ApplyAsync(
                new ProductSqlAuthorityProjectionWriteRequest(
                    tenantId,
                    productId,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = productId,
                        Price = 999m,
                        DefaultUnit = new ProductUnitDocument
                        {
                            UnitId = Guid.NewGuid(),
                            Name = "Stale",
                            SalePrice = 999m
                        }
                    },
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    EventVersion: 10)));

        Assert.Equal("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_REQUIRED", ex.Message);
        Assert.Null(capturedProjection);
    }

    [Fact]
    public async Task ApplyAsync_TombstonesThroughReviewedMutationBoundaryWhenSqlAuthorityIsNotVisible()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        SeedActiveTenantAndCategory(dbContext, tenantId, categoryId);
        var product = CreateVisibleProduct(tenantId, productId, categoryId);
        product.IsOnlineVisible = false;
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var repository = CreateRepositoryMock(_ => { }, tombstoneApplied: true);
        var mutationBoundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var writer = new ProductSqlAuthorityProjectionWriter(dbContext, mutationBoundary);

        var eventId = Guid.NewGuid();
        var eventOccurredAtUtc = DateTime.UtcNow;

        var result = await writer.ApplyAsync(
            new ProductSqlAuthorityProjectionWriteRequest(
                tenantId,
                productId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    IsActive = true,
                    IsOnlineVisible = true
                },
                eventId,
                eventOccurredAtUtc,
                EventVersion: 12));

        Assert.Equal(ProductSqlAuthorityProjectionWriteDisposition.Tombstoned, result.Disposition);
        var expectedGeneration = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        repository.Verify(
            item => item.TombstoneIfFreshAsync(
                tenantId,
                productId,
                eventOccurredAtUtc,
                expectedGeneration,
                eventId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StorefrontProductProjectionMutationBoundary_RejectsUnreviewedFullDocumentUpsert()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new Mock<IProductReadRepository>(MockBehavior.Loose);
        var boundary = new StorefrontProductProjectionMutationBoundary(repository.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ApplyAsync(
                new StorefrontProductProjectionMutation(
                    StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert,
                    tenantId,
                    productId,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = productId,
                        IsActive = true,
                        IsOnlineVisible = true
                    },
                    DateTime.UtcNow,
                    EventVersion: 1,
                    Guid.NewGuid(),
                    ReviewReason: string.Empty)));

        Assert.Equal("STOREFRONT_PRODUCT_MUTATION_REVIEW_REASON_REQUIRED", ex.Message);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StorefrontProductProjectionMutationBoundary_RejectsInvisibleFullDocumentUpsert()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new Mock<IProductReadRepository>(MockBehavior.Loose);
        var boundary = new StorefrontProductProjectionMutationBoundary(repository.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ApplyAsync(
                new StorefrontProductProjectionMutation(
                    StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert,
                    tenantId,
                    productId,
                    new ProductDocument
                    {
                        TenantId = tenantId,
                        ProductId = productId,
                        IsActive = true,
                        IsOnlineVisible = false
                    },
                    DateTime.UtcNow,
                    EventVersion: 1,
                    Guid.NewGuid(),
                    ReviewReason: "unit test")));

        Assert.Equal("STOREFRONT_PRODUCT_UPSERT_REQUIRES_VISIBLE_SQL_PROJECTION", ex.Message);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StorefrontProductProjectionAdmission_RejectsStaleProjectionVersion()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new Mock<IProductReadRepository>(MockBehavior.Loose);
        var boundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var projection = CreateAdmissibleSimpleProjection(tenantId, productId);
        projection.ProjectionVersion = ProductDocument.CurrentProjectionVersion - 1;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ApplyAsync(CreateUpsertMutation(tenantId, productId, projection)));

        Assert.Equal("STOREFRONT_PRODUCT_ADMISSION_PROJECTION_VERSION_STALE", ex.Message);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StorefrontProductProjectionAdmission_RejectsInvalidDefaultUnit()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new Mock<IProductReadRepository>(MockBehavior.Loose);
        var boundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var projection = CreateAdmissibleSimpleProjection(tenantId, productId);
        projection.DefaultUnit!.SalePrice = 0m;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ApplyAsync(CreateUpsertMutation(tenantId, productId, projection)));

        Assert.Equal("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_PRICE_INVALID", ex.Message);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StorefrontProductProjectionAdmission_RejectsMissingVariantId()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var repository = new Mock<IProductReadRepository>(MockBehavior.Loose);
        var boundary = new StorefrontProductProjectionMutationBoundary(repository.Object);
        var projection = CreateAdmissibleVariantProjection(tenantId, productId);
        projection.Variants[0].VariantId = Guid.Empty;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ApplyAsync(CreateUpsertMutation(tenantId, productId, projection)));

        Assert.Equal("STOREFRONT_PRODUCT_ADMISSION_VARIANT_ID_REQUIRED", ex.Message);
        repository.Verify(
            item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IProductReadRepository> CreateRepositoryMock(
        Action<ProductDocument> capture,
        bool tombstoneApplied = false)
    {
        var repository = new Mock<IProductReadRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.UpsertAsync(It.IsAny<ProductDocument>(), It.IsAny<CancellationToken>()))
            .Callback<ProductDocument, CancellationToken>((document, _) => capture(document))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.TombstoneIfFreshAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tombstoneApplied);
        return repository;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedActiveTenantAndCategory(ApplicationDbContext dbContext, Guid tenantId, Guid categoryId)
    {
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Projection tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Projection category",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }

    private static Product CreateVisibleProduct(Guid tenantId, Guid productId, Guid categoryId)
    {
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Test Product",
            SKU = "SKU-1",
            CategoryId = categoryId,
            PurchasePrice = 42m,
            SalePrice = 100m,
            OnlinePrice = 110m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = false,
            TrackStock = true,
            AllowBackorder = false,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        product.AdvanceProjectionMutationVersion(1);
        return product;
    }

    private static PointOfSale.Core.Entities.Unit CreateUnit(Guid tenantId, Guid unitId, string name, string symbol)
    {
        return new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = name,
            Symbol = symbol,
            IsActive = true
        };
    }

    private static ProductUnit CreateProductUnit(
        Guid productId,
        Guid unitId,
        PointOfSale.Core.Entities.Unit unit,
        decimal salePrice,
        decimal conversionFactor,
        bool isDefault,
        bool isBaseUnit,
        decimal? purchasePrice = null)
    {
        return new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UnitId = unitId,
            Unit = unit,
            PurchasePrice = purchasePrice,
            SellingPrice = salePrice,
            ConversionFactor = conversionFactor,
            IsDefault = isDefault,
            IsBaseUnit = isBaseUnit
        };
    }

    private static StorefrontProductProjectionMutation CreateUpsertMutation(
        Guid tenantId,
        Guid productId,
        ProductDocument projection)
    {
        return new StorefrontProductProjectionMutation(
            StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert,
            tenantId,
            productId,
            projection,
            DateTime.UtcNow,
            EventVersion: 22,
            Guid.NewGuid(),
            ReviewReason: "unit test admission");
    }

    private static ProductDocument CreateAdmissibleSimpleProjection(Guid tenantId, Guid productId)
    {
        var unitId = Guid.NewGuid();
        return new ProductDocument
        {
            TenantId = tenantId,
            ProductId = productId,
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = false,
            LastEventId = Guid.NewGuid(),
            LastEventOccurredAtUtc = DateTime.UtcNow,
            LastEventVersion = 22,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = unitId,
                Name = "Piece",
                SalePrice = 25m,
                ConversionFactor = 1m,
                IsBaseUnit = true
            },
            Units =
            [
                new ProductUnitDocument
                {
                    UnitId = unitId,
                    Name = "Piece",
                    SalePrice = 25m,
                    ConversionFactor = 1m,
                    IsBaseUnit = true
                }
            ]
        };
    }

    private static ProductDocument CreateAdmissibleVariantProjection(Guid tenantId, Guid productId)
    {
        return new ProductDocument
        {
            TenantId = tenantId,
            ProductId = productId,
            IsActive = true,
            IsOnlineVisible = true,
            HasVariants = true,
            LastEventId = Guid.NewGuid(),
            LastEventOccurredAtUtc = DateTime.UtcNow,
            LastEventVersion = 22,
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = Guid.NewGuid(),
                    IsActive = true,
                    IsDefault = true,
                    Price = 25m,
                    TotalStock = 5m
                }
            ]
        };
    }
}
