using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Mappers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Products;

public sealed class ProductResponseMapperTests
{
    [Fact]
    public void ToResponseDto_UsesOrderedCanonicalImageMediaBeforeLegacyImages()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Media product",
            Images = "https://legacy.example.test/image.jpg"
        };
        product.Media.Add(new ProductMedia
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProductId = productId,
            MediaType = "image", Url = "/media/second.png", Position = 2,
            CreatedAtUtc = DateTime.UtcNow
        });
        product.Media.Add(new ProductMedia
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProductId = productId,
            MediaType = "video", Url = "/media/video.mp4", Position = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        product.Media.Add(new ProductMedia
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProductId = productId,
            MediaType = "image", Url = "/media/primary.png", Position = 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        var response = product.ToResponseDto();

        Assert.Equal("/media/primary.png,/media/second.png", response.Images);
    }

    [Fact]
    public void ToResponseDto_ExcludesDeletedAndSupersededVariants_AndReportsAvailableStock()
    {
        var tenantId = Guid.NewGuid();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Variant truth",
            HasVariants = true
        };
        var current = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            Price = 400m,
            OnlinePrice = null,
            CostPrice = 125m,
            IsActive = false,
            IsDefault = true,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Current
        };
        current.Stocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = current.Id,
            Variant = current,
            WarehouseId = Guid.NewGuid(),
            Quantity = 10m,
            ReservedQuantity = 3m
        });
        var superseded = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            Price = 999m,
            IsActive = true,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
            SemanticSupersededAtUtc = DateTime.UtcNow
        };
        superseded.Stocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = superseded.Id,
            Variant = superseded,
            WarehouseId = current.Stocks.Single().WarehouseId,
            Quantity = 100m
        });
        var deleted = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            Price = 888m,
            IsActive = false,
            IsDeleted = true
        };
        product.Variants.Add(current);
        product.Variants.Add(superseded);
        product.Variants.Add(deleted);

        var response = product.ToResponseDto();

        var variant = Assert.Single(response.Variants);
        Assert.Equal(current.Id, variant.Id);
        Assert.False(variant.IsActive);
        Assert.Equal(400m, variant.Price);
        Assert.Null(variant.OnlinePrice);
        Assert.Equal(125m, variant.CostPrice);
        Assert.Equal(7m, variant.TotalStock);
        var warehouseStock = Assert.Single(response.WarehouseStocks);
        Assert.Equal(10m, warehouseStock.Quantity);
        Assert.Equal(3m, warehouseStock.ReservedQuantity);
        Assert.Equal(7m, warehouseStock.AvailableQuantity);
    }

    [Fact]
    public async Task GetProducts_UsesAvailableStockForEveryStockStateAndSortDirection()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var category = new Category { Id = categoryId, TenantId = tenantId, Name = "Stock category" };
        var warehouse = new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Main" };
        var lowStock = CreateSimpleProductWithStock(
            tenantId, categoryId, category, warehouse, "Low stock", quantity: 50m, reservedQuantity: 47m, lowStockThreshold: 5);
        var supersededVariant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = lowStock.Id,
            Product = lowStock,
            SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged,
            SemanticSupersededAtUtc = DateTime.UtcNow
        };
        supersededVariant.Stocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = supersededVariant.Id,
            Variant = supersededVariant,
            WarehouseId = warehouseId,
            Warehouse = warehouse,
            Quantity = 100m
        });
        lowStock.Variants.Add(supersededVariant);
        var healthyStock = CreateSimpleProductWithStock(
            tenantId, categoryId, category, warehouse, "Healthy stock", quantity: 20m, reservedQuantity: 0m, lowStockThreshold: 5);
        var outOfStock = CreateSimpleProductWithStock(
            tenantId, categoryId, category, warehouse, "Reserved out", quantity: 100m, reservedQuantity: 100m, lowStockThreshold: 5);

        dbContext.AddRange(category, warehouse, lowStock, healthyStock, outOfStock);
        await dbContext.SaveChangesAsync();

        var controller = CreateProductsController(dbContext, tenantId);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var lowStockResult = await controller.GetProducts(
            search: null,
            categoryId: null,
            isActive: null,
            stockState: "lowStock");
        var lowStockOk = Assert.IsType<OkObjectResult>(lowStockResult.Result);
        var lowStockRows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(lowStockOk.Value).ToList();
        Assert.Equal("Low stock", Assert.Single(lowStockRows).Name);
        Assert.Equal("1", controller.Response.Headers["X-Total-Count"]);

        var outOfStockResult = await controller.GetProducts(
            search: null,
            categoryId: null,
            isActive: null,
            stockState: "outOfStock");
        var outOfStockOk = Assert.IsType<OkObjectResult>(outOfStockResult.Result);
        var outOfStockRows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(outOfStockOk.Value).ToList();
        Assert.Equal("Reserved out", Assert.Single(outOfStockRows).Name);

        var inStockResult = await controller.GetProducts(
            search: null,
            categoryId: null,
            isActive: null,
            stockState: "inStock");
        var inStockOk = Assert.IsType<OkObjectResult>(inStockResult.Result);
        var inStockRows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(inStockOk.Value).ToList();
        Assert.Equal("Healthy stock", Assert.Single(inStockRows).Name);

        var sortedResult = await controller.GetProducts(
            search: null,
            categoryId: null,
            isActive: null,
            pageSize: 1,
            sortBy: "stock",
            sortDirection: "desc");
        var sortedOk = Assert.IsType<OkObjectResult>(sortedResult.Result);
        var sortedRows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(sortedOk.Value).ToList();
        Assert.Equal("Healthy stock", Assert.Single(sortedRows).Name);
        Assert.Equal("3", controller.Response.Headers["X-Total-Count"]);

        var ascendingResult = await controller.GetProducts(
            search: null,
            categoryId: null,
            isActive: null,
            pageSize: 1,
            sortBy: "stock",
            sortDirection: "asc");
        var ascendingOk = Assert.IsType<OkObjectResult>(ascendingResult.Result);
        var ascendingRows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(ascendingOk.Value).ToList();
        Assert.Equal("Reserved out", Assert.Single(ascendingRows).Name);
    }

    [Fact]
    public async Task GetProducts_AppliesCoreTableFiltersAndSortBeforePagination()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Table category"
        });
        dbContext.Products.AddRange(
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Alpha matching",
                HasVariants = true,
                IsActive = true,
                PublishStatus = PublishStatus.Archived,
                IsFeatured = true,
                IsSearchable = true,
                AllowBackorder = true
            },
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Zulu matching",
                HasVariants = true,
                IsActive = true,
                PublishStatus = PublishStatus.Archived,
                IsFeatured = true,
                IsSearchable = true,
                AllowBackorder = true
            },
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CategoryId = categoryId,
                Name = "Nonmatching draft",
                HasVariants = true,
                IsActive = true,
                PublishStatus = PublishStatus.Draft,
                IsFeatured = true,
                IsSearchable = true,
                AllowBackorder = true
            },
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                CategoryId = categoryId,
                Name = "Foreign tenant",
                HasVariants = true,
                IsActive = true,
                PublishStatus = PublishStatus.Archived,
                IsFeatured = true,
                IsSearchable = true,
                AllowBackorder = true
            });
        await dbContext.SaveChangesAsync();

        var controller = CreateProductsController(dbContext, tenantId);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.GetProducts(
            search: null,
            categoryId: categoryId,
            isActive: true,
            page: 1,
            pageSize: 1,
            hasVariants: true,
            publishStatus: PublishStatus.Archived,
            isFeatured: true,
            isSearchable: true,
            allowBackorder: true,
            sortBy: "name",
            sortDirection: "desc");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsAssignableFrom<IEnumerable<ProductResponseDto>>(ok.Value).ToList();
        var row = Assert.Single(rows);

        Assert.Equal("Zulu matching", row.Name);
        Assert.Equal("2", controller.Response.Headers["X-Total-Count"]);
        Assert.Equal("2", controller.Response.Headers["X-Total-Pages"]);
    }

    [Fact]
    public void ToResponseDto_IncludesProductUnitPurchasePrice_InCamelCaseJson()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productUnitId = Guid.NewGuid();

        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc"
        };

        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Simple product",
            CategoryId = categoryId,
            Category = new Category
            {
                Id = categoryId,
                TenantId = tenantId,
                Name = "Category"
            },
            PurchasePrice = 999m,
            SalePrice = 1199m,
            HasVariants = false,
            ProductUnits =
            {
                new ProductUnit
                {
                    Id = productUnitId,
                    ProductId = productId,
                    UnitId = unitId,
                    Unit = unit,
                    ConversionFactor = 1m,
                    PurchasePrice = 1000m,
                    SellingPrice = 1200m,
                    IsDefault = true,
                    IsBaseUnit = true
                }
            }
        };

        var response = product.ToResponseDto();
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var responseUnit = Assert.Single(response.Units);
        Assert.Equal(1000m, responseUnit.PurchasePrice);
        Assert.Equal(1200m, responseUnit.SellingPrice);
        Assert.Equal(1000m, response.PurchasePrice);
        Assert.Equal(1200m, response.SalePrice);

        var jsonUnit = root.GetProperty("units")[0];
        Assert.Equal(1000m, jsonUnit.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(1200m, jsonUnit.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(1000m, root.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(1200m, root.GetProperty("salePrice").GetDecimal());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("variants").ValueKind);
        Assert.Empty(root.GetProperty("variants").EnumerateArray());
    }

    [Fact]
    public void ToResponseDto_RehydratesLegacyFaqCasingAndAiMetadataWithoutNullFailures()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Structured content",
            FaqJson = """
                [
                  {"question":" Camel question ","answer":" Camel answer "},
                  {"Question":" Pascal question ","Answer":null},
                  {"question":null,"answer":null}
                ]
                """,
            AiGenerated = true,
            AiMetadataJson = "{\"source\":\"admin_ai_product_generator\",\"score\":9}"
        };

        var response = product.ToResponseDto();

        Assert.Collection(
            response.Faqs,
            item =>
            {
                Assert.Equal("Camel question", item.Question);
                Assert.Equal("Camel answer", item.Answer);
            },
            item =>
            {
                Assert.Equal("Pascal question", item.Question);
                Assert.Equal(string.Empty, item.Answer);
            });
        Assert.True(response.AiGenerated);
        Assert.Equal("admin_ai_product_generator", response.AiMetadata?.GetProperty("source").GetString());
        Assert.Equal(9, response.AiMetadata?.GetProperty("score").GetInt32());

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Camel question", document.RootElement.GetProperty("faqs")[0].GetProperty("question").GetString());
        Assert.Equal("admin_ai_product_generator", document.RootElement.GetProperty("aiMetadata").GetProperty("source").GetString());
    }

    [Fact]
    public void ToResponseDto_MalformedLegacyStructuredJsonReturnsSafeEmptyValues()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Malformed legacy content",
            FaqJson = "not-json",
            AiMetadataJson = "{broken"
        };

        var response = product.ToResponseDto();

        Assert.Empty(response.Faqs);
        Assert.Null(response.AiMetadata);
    }

    [Fact]
    public async Task GetProduct_ReturnsSavedProductUnitPurchasePrice_InRuntimeJsonResponse()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productUnitId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc"
        };

        var category = new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Category"
        };

        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Simple product",
            CategoryId = categoryId,
            Category = category,
            PurchasePrice = 999m,
            SalePrice = 1199m,
            HasVariants = false,
            IsActive = true
        };

        product.ProductUnits.Add(new ProductUnit
        {
            Id = productUnitId,
            ProductId = productId,
            Product = product,
            UnitId = unitId,
            Unit = unit,
            ConversionFactor = 1m,
            PurchasePrice = 1000m,
            SellingPrice = 1200m,
            IsDefault = true,
            IsBaseUnit = true
        });

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var controller = CreateProductsController(dbContext, tenantId);

        var result = await controller.GetProduct(productId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ProductResponseDto>(ok.Value);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var responseUnit = Assert.Single(response.Units);
        var jsonUnit = root.GetProperty("units")[0];

        Assert.Equal(1000m, responseUnit.PurchasePrice);
        Assert.Equal(1200m, responseUnit.SellingPrice);
        Assert.Equal(1000m, jsonUnit.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(1200m, jsonUnit.GetProperty("sellingPrice").GetDecimal());
        Assert.Equal(1000m, root.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal(1200m, root.GetProperty("salePrice").GetDecimal());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("variants").ValueKind);
        Assert.Empty(root.GetProperty("variants").EnumerateArray());
    }

    private static ProductsController CreateProductsController(ApplicationDbContext dbContext, Guid tenantId)
    {
        var tenantContext = new TestTenantContext(tenantId);

        return new ProductsController(
            dbContext,
            tenantContext,
            NullLogger<ProductsController>.Instance,
            Mock.Of<IBarcodeService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IAuditService>(),
            Mock.Of<IProductVariantService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IMarketplaceEventOutboxService>(),
            Mock.Of<IProductAuthoringOrchestrator>(),
            Mock.Of<IProductAuthoringCommandMapper>(),
            Mock.Of<IProductAuthoringReconstructionService>(),
            Mock.Of<IProductMutationIdempotencyService>(),
            Mock.Of<IBulkProductLifecycleService>());
    }

    private static Product CreateSimpleProductWithStock(
        Guid tenantId,
        Guid categoryId,
        Category category,
        Warehouse warehouse,
        string name,
        decimal quantity,
        decimal reservedQuantity,
        int lowStockThreshold)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Category = category,
            Name = name,
            HasVariants = false,
            IsActive = true,
            TrackStock = true,
            LowStockThreshold = lowStockThreshold
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            IsDefault = true,
            IsActive = true
        };
        variant.Stocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variant.Id,
            Variant = variant,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Quantity = quantity,
            ReservedQuantity = reservedQuantity
        });
        product.Variants.Add(variant);
        return product;
    }
}
