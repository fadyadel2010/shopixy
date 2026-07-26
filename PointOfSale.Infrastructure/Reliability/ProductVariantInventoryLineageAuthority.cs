using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative inventory lineage guard for variant stock and reservations.
/// Stock events, reservation rows, warehouse rows, and replay payloads are evidence only;
/// current PostgreSQL variant, product, warehouse, stock, and reservation lineage owns
/// whether inventory can be reserved, consumed, or propagated.
/// </summary>
public static class ProductVariantInventoryLineageAuthority
{
    public static IQueryable<ProductVariant> WhereInventoryReservableLineage(
        this IQueryable<ProductVariant> query,
        Guid tenantId)
    {
        return query.Where(variant =>
            variant.TenantId == tenantId &&
            !variant.IsDeleted &&
            variant.IsActive &&
            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
            variant.SemanticSupersededAtUtc == null &&
            variant.SemanticReplacementVariantId == null &&
            variant.ProductId != Guid.Empty &&
            variant.Product.TenantId == tenantId &&
            !variant.Product.IsDeleted &&
            variant.Product.IsActive);
    }

    public static IQueryable<ProductVariantStock> WhereCurrentInventoryStockOwner(
        this IQueryable<ProductVariantStock> query,
        Guid tenantId)
    {
        return query.Where(stock =>
            stock.TenantId == tenantId &&
            !stock.IsDeleted &&
            stock.Warehouse.TenantId == tenantId &&
            !stock.Warehouse.IsDeleted &&
            stock.Warehouse.IsActive &&
            stock.Variant.TenantId == tenantId &&
            !stock.Variant.IsDeleted &&
            stock.Variant.IsActive &&
            stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
            stock.Variant.SemanticSupersededAtUtc == null &&
            stock.Variant.SemanticReplacementVariantId == null &&
            stock.Variant.ProductId != Guid.Empty &&
            stock.Variant.Product.TenantId == tenantId &&
            !stock.Variant.Product.IsDeleted &&
            stock.Variant.Product.IsActive);
    }

    public static async Task<bool> IsVariantReservableForInventoryAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        var reservable = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereInventoryReservableLineage(tenantId)
            .AnyAsync(variant => variant.Id == variantId, cancellationToken);

        if (!reservable)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "variant_inventory_reservation",
                OperationalConvergenceAuthority.ProductVariantInventory,
                OperationalConvergenceReason.VariantInventoryLineageLost,
                tenantId);
        }

        return reservable;
    }

    public static async Task RequireCurrentStockMutationOwnerAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid variantId,
        Guid warehouseId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var valid = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereInventoryReservableLineage(tenantId)
            .AnyAsync(variant =>
                    variant.Id == variantId &&
                    dbContext.Warehouses
                        .IgnoreQueryFilters()
                        .Any(warehouse =>
                            warehouse.TenantId == tenantId &&
                            warehouse.Id == warehouseId &&
                            !warehouse.IsDeleted &&
                            warehouse.IsActive),
                cancellationToken);

        if (valid)
        {
            return;
        }

        OperationalConvergenceTelemetry.RecordAuthorityFailure(
            "variant_stock_mutation",
            OperationalConvergenceAuthority.ProductVariantInventory,
            OperationalConvergenceReason.VariantInventoryLineageLost,
            tenantId);

        throw new InvalidOperationException(
            $"VARIANT_INVENTORY_LINEAGE_OWNER_REQUIRED: Variant {variantId} in warehouse {warehouseId} is not current inventory authority for {operation}.");
    }

    public static async Task<List<ProductVariantStock>> FilterCurrentInventoryStockOwnersAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IReadOnlyCollection<ProductVariantStock> stocks,
        CancellationToken cancellationToken = default)
    {
        if (stocks.Count == 0)
        {
            return new List<ProductVariantStock>();
        }

        var stockIds = stocks.Select(stock => stock.Id).Distinct().ToList();
        var validStockIds = await dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => stockIds.Contains(stock.Id))
            .Select(stock => stock.Id)
            .ToListAsync(cancellationToken);

        var validSet = validStockIds.ToHashSet();
        var current = stocks.Where(stock => validSet.Contains(stock.Id)).ToList();
        var suppressedCount = stocks.Count - current.Count;
        if (suppressedCount > 0)
        {
            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                "variant_stock_visibility",
                OperationalConvergenceAuthority.ProductVariantInventory,
                OperationalConvergenceDecision.Suppressed,
                OperationalConvergenceReason.WarehouseStockLineageLost,
                tenantId,
                suppressedCount);
        }

        return current;
    }

    public static async Task<List<InventoryReservation>> FilterCurrentConsumableReservationsAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IReadOnlyCollection<InventoryReservation> reservations,
        DateTime nowUtc,
        CancellationToken cancellationToken = default,
        bool allowUnboundProductId = false)
    {
        if (reservations.Count == 0)
        {
            return new List<InventoryReservation>();
        }

        var reservationIds = reservations
            .Select(reservation => reservation.Id)
            .Distinct()
            .ToList();

        var validReservationIds = await (
                from reservation in dbContext.InventoryReservations.IgnoreQueryFilters().AsNoTracking()
                join stock in dbContext.ProductVariantStocks.IgnoreQueryFilters().AsNoTracking()
                    on new
                    {
                        reservation.TenantId,
                        reservation.VariantId,
                        reservation.WarehouseId
                    }
                    equals new
                    {
                        stock.TenantId,
                        stock.VariantId,
                        stock.WarehouseId
                    }
                join warehouse in dbContext.Warehouses.IgnoreQueryFilters().AsNoTracking()
                    on new { stock.TenantId, stock.WarehouseId }
                    equals new { warehouse.TenantId, WarehouseId = warehouse.Id }
                join variant in dbContext.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                    on new { reservation.TenantId, reservation.VariantId }
                    equals new { variant.TenantId, VariantId = variant.Id }
                join product in dbContext.Products.IgnoreQueryFilters().AsNoTracking()
                    on new { variant.TenantId, variant.ProductId }
                    equals new { product.TenantId, ProductId = product.Id }
                where reservation.TenantId == tenantId &&
                      reservationIds.Contains(reservation.Id) &&
                      !reservation.IsDeleted &&
                      reservation.Status == InventoryReservationStatus.Active &&
                      reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                      (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc) &&
                      !stock.IsDeleted &&
                      stock.ReservedQuantity > 0m &&
                      !warehouse.IsDeleted &&
                      warehouse.IsActive &&
                      !variant.IsDeleted &&
                      variant.IsActive &&
                      variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                      variant.SemanticSupersededAtUtc == null &&
                      variant.SemanticReplacementVariantId == null &&
                      !product.IsDeleted &&
                      product.IsActive &&
                      (reservation.ProductId == product.Id ||
                       (allowUnboundProductId && reservation.ProductId == Guid.Empty))
                select reservation.Id)
            .ToListAsync(cancellationToken);

        var validSet = validReservationIds.ToHashSet();
        var current = reservations.Where(reservation => validSet.Contains(reservation.Id)).ToList();
        var suppressedCount = reservations.Count - current.Count;
        if (suppressedCount > 0)
        {
            OperationalConvergenceTelemetry.RecordReplayDecision(
                OperationalConvergencePhase.Replay,
                "variant_reservation_consumption",
                OperationalConvergenceAuthority.ProductVariantInventory,
                OperationalConvergenceDecision.Suppressed,
                OperationalConvergenceReason.VariantInventoryLineageLost,
                tenantId,
                suppressedCount);
        }

        return current;
    }

    public static async Task<List<InventoryReservation>> FilterCurrentConsumableReservationsAcrossTenantsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<InventoryReservation> reservations,
        DateTime nowUtc,
        CancellationToken cancellationToken = default,
        bool allowUnboundProductId = false)
    {
        if (reservations.Count == 0)
        {
            return new List<InventoryReservation>();
        }

        var current = new List<InventoryReservation>(reservations.Count);

        foreach (var tenantGroup in reservations
                     .GroupBy(reservation => reservation.TenantId)
                     .OrderBy(group => group.Key))
        {
            var tenantCurrent = await FilterCurrentConsumableReservationsAsync(
                dbContext,
                tenantGroup.Key,
                tenantGroup.ToList(),
                nowUtc,
                cancellationToken,
                allowUnboundProductId);

            current.AddRange(tenantCurrent);
        }

        return current;
    }
}
