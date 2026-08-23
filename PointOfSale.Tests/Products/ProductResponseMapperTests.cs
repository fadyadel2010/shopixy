using System.Text.Json;
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
}
