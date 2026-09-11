using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

public sealed class CatalogDependencyProjectionPropagationTests
{
    [Fact]
    public async Task DependencyMutations_StageParentProductTriggerAtPersistedGeneration()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var product = CreateProduct(tenantId, categoryId);
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = unitId,
            TenantId = tenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "DEPENDENCY-VARIANT",
            Price = 10m,
            IsActive = true,
            IsDefault = true
        };

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Original category"
        });
        context.Products.Add(product);
        context.Units.Add(unit);
        context.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unitId,
            Unit = unit,
            ConversionFactor = 1m,
            SellingPrice = 10m,
            IsBaseUnit = true,
            IsDefault = true
        });
        context.ProductVariants.Add(variant);
        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Dependency warehouse",
            IsActive = true
        });
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variant.Id,
            WarehouseId = warehouseId,
            Quantity = 5m
        });
        await context.SaveChangesAsync();

        var events = new List<ProductUpdatedEvent>();
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(item => item.PublishAsync(
                It.IsAny<ProductUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductUpdatedEvent, CancellationToken>((@event, _) => events.Add(@event))
            .Returns(Task.CompletedTask);

        await AssertMutationAsync(
            () => CatalogDependencyProjectionPropagation.PublishCategoryMutationAsync(
                context, publisher.Object, tenantId, categoryId, default),
            "Category");
        await AssertMutationAsync(
            () => CatalogDependencyProjectionPropagation.PublishUnitMutationAsync(
                context, publisher.Object, tenantId, unitId, default),
            "Units");
        await AssertMutationAsync(
            () => CatalogDependencyProjectionPropagation.PublishWarehouseMutationAsync(
                context, publisher.Object, tenantId, warehouseId, default),
            "Stock");

        async Task AssertMutationAsync(Func<Task> propagate, string changedField)
        {
            events.Clear();
            var previousGeneration = product.ProjectionMutationVersion;

            await propagate();
            var stagedEvent = Assert.Single(events);
            await context.SaveChangesAsync();

            Assert.True(product.ProjectionMutationVersion > previousGeneration);
            Assert.Equal(product.ProjectionMutationVersion, stagedEvent.EventVersion);
            Assert.Equal(product.Id, stagedEvent.ProductId);
            Assert.Equal(tenantId, stagedEvent.TenantId);
            Assert.Equal(new[] { changedField }, stagedEvent.ChangedFields);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"catalog-dependency-propagation-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Product CreateProduct(Guid tenantId, Guid categoryId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = "Dependency product",
        SKU = $"DEPENDENCY-{Guid.NewGuid():N}",
        CategoryId = categoryId,
        IsActive = true,
        IsOnlineVisible = true,
        IsSearchable = true,
        PublishStatus = PublishStatus.Published
    };
}
