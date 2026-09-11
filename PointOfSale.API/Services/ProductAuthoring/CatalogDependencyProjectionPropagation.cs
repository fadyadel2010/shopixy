using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.ProductAuthoring;

public static class CatalogDependencyProjectionPropagation
{
    public static async Task PublishCategoryMutationAsync(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        Guid tenantId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var productIds = await dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                product.CategoryId == categoryId &&
                !product.IsDeleted)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);

        await PublishProductTriggersAsync(
            dbContext,
            eventPublisher,
            tenantId,
            productIds,
            $"category:{categoryId:N}",
            "Category",
            cancellationToken);
    }

    public static async Task PublishUnitMutationAsync(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var productIds = await dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(productUnit => productUnit.UnitId == unitId && !productUnit.IsDeleted)
            .Select(productUnit => productUnit.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        await PublishProductTriggersAsync(
            dbContext,
            eventPublisher,
            tenantId,
            productIds,
            $"unit:{unitId:N}",
            "Units",
            cancellationToken);
    }

    public static async Task PublishWarehouseMutationAsync(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        Guid tenantId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var productIds = await (
                from stock in dbContext.ProductVariantStocks.AsNoTracking().IgnoreQueryFilters()
                join variant in dbContext.ProductVariants.AsNoTracking().IgnoreQueryFilters()
                    on new { stock.TenantId, stock.VariantId }
                    equals new { variant.TenantId, VariantId = variant.Id }
                where stock.TenantId == tenantId &&
                      stock.WarehouseId == warehouseId &&
                      !stock.IsDeleted &&
                      !variant.IsDeleted
                select variant.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        await PublishProductTriggersAsync(
            dbContext,
            eventPublisher,
            tenantId,
            productIds,
            $"warehouse:{warehouseId:N}",
            "Stock",
            cancellationToken);
    }

    private static async Task PublishProductTriggersAsync(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        string dependencyKey,
        string changedField,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        var products = await dbContext.Products
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                productIds.Contains(product.Id) &&
                !product.IsDeleted)
            .ToListAsync(cancellationToken);
        var changedAtUtc = DateTime.UtcNow;

        foreach (var product in products)
        {
            product.UpdatedAt = changedAtUtc;
            var pendingGeneration = ProductEventVersionHelper.ToProductEventVersion(
                product.ProjectionMutationVersion + 1);

            await eventPublisher.PublishAsync(
                new ProductUpdatedEvent
                {
                    TenantId = tenantId,
                    ProductId = product.Id,
                    EventVersion = pendingGeneration,
                    IdempotencyKey = $"catalog-dependency:{dependencyKey}:product:{product.Id:N}:generation:{pendingGeneration}",
                    ChangedFields = [changedField]
                },
                cancellationToken);
        }
    }
}
