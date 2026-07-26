using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// PHASE: FINAL HARDENING - Inventory Service with mandatory TenantId
///
/// CRITICAL:
/// - ProductVariantStocks is the ONLY mutable stock source of truth.
/// - Low-level adjustment and transfer callers MUST own the transaction boundary and SaveChanges/Commit lifecycle.
/// - Complete inventory operations exposed by this service own their own transaction boundary.
/// - Every mutation locks the row before changing quantities to prevent lost updates.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        ApplicationDbContext context, 
        ITenantContext tenantContext,
        ILogger<InventoryService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<StockMovement> AdjustStockAsync(Guid warehouseId, Guid productId, decimal quantityChange, StockMovementType type, string reason, Guid? productVariantId = null, Guid? referenceId = null, string? referenceNumber = null)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
            _context,
            productId,
            productVariantId);

        if (variant == null)
        {
            throw new InvalidOperationException(
                $"Unable to resolve an active variant for product {productId}.");
        }

        // Lock BEFORE mutation so concurrent requests serialize on the exact stock row.
        var stock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
            _context,
            tenantId,
            warehouseId,
            variant.Id,
            createIfMissing: quantityChange > 0);

        if (stock == null)
        {
            throw new InvalidOperationException("Cannot decrease stock for a non-existent stock record.");
        }

        ProductVariantStockMutationSupport.ValidateQuantityChange(
            stock,
            quantityChange,
            allowNegativeStock: false,
            operationName: type.ToString());

        var utcNow = DateTime.UtcNow;
        stock.Quantity += quantityChange;
        stock.UpdatedAt = utcNow;

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            ProductId = variant.ProductId,
            ProductVariantId = variant.Id,
            MovementType = type,
            QuantityChange = quantityChange,
            QuantityAfter = stock.Quantity,
            AvailableAfter = stock.AvailableQuantity,
            ReservedAfter = stock.ReservedQuantity,
            DamagedAfter = stock.DamagedQuantity,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            Notes = reason,
            CreatedAt = utcNow
        };

        _context.StockMovements.Add(movement);

        _logger.LogInformation(
            "Stock adjustment staged with locked ProductVariantStock | TenantId: {TenantId} | Type: {Type} | Qty: {Qty} | After: {After} | Warehouse: {Warehouse} | VariantId: {VariantId}",
            tenantId, type, quantityChange, stock.Quantity, warehouseId, variant.Id);

        return movement;
    }

    public async Task<StockMovement> TransferStockAsync(Guid sourceWarehouseId, Guid targetWarehouseId, Guid productId, decimal quantity, string notes, Guid? productVariantId = null)
    {
        if (quantity <= 0) throw new ArgumentException("Transfer quantity must be positive.", nameof(quantity));
        if (sourceWarehouseId == targetWarehouseId)
        {
            throw new ArgumentException("Source and target warehouses must be different.", nameof(targetWarehouseId));
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
            _context,
            productId,
            productVariantId);

        if (variant == null)
        {
            throw new InvalidOperationException(
                $"Unable to resolve an active variant for product {productId}.");
        }

        // Deterministic multi-row locking prevents deadlocks across concurrent transfers.
        var lockedStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
            _context,
            tenantId,
            new[]
            {
                new StockLockTarget(sourceWarehouseId, variant.Id, CreateIfMissing: false),
                new StockLockTarget(targetWarehouseId, variant.Id, CreateIfMissing: true)
            });

        if (!lockedStocks.TryGetValue((sourceWarehouseId, variant.Id), out var sourceStock))
        {
            throw new InvalidOperationException("Cannot transfer stock from a non-existent source stock record.");
        }

        if (!lockedStocks.TryGetValue((targetWarehouseId, variant.Id), out var targetStock))
        {
            throw new InvalidOperationException("Unable to materialize the target stock record for transfer.");
        }

        ProductVariantStockMutationSupport.ValidateQuantityChange(
            sourceStock,
            -quantity,
            allowNegativeStock: false,
            operationName: StockMovementType.TransferOut.ToString());

        var utcNow = DateTime.UtcNow;
        sourceStock.Quantity -= quantity;
        sourceStock.UpdatedAt = utcNow;

        targetStock.Quantity += quantity;
        targetStock.UpdatedAt = utcNow;

        _context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = sourceWarehouseId,
            ToWarehouseId = targetWarehouseId,
            ProductId = variant.ProductId,
            ProductVariantId = variant.Id,
            MovementType = StockMovementType.TransferOut,
            QuantityChange = -quantity,
            QuantityAfter = sourceStock.Quantity,
            AvailableAfter = sourceStock.AvailableQuantity,
            ReservedAfter = sourceStock.ReservedQuantity,
            DamagedAfter = sourceStock.DamagedQuantity,
            ReferenceId = targetWarehouseId,
            ReferenceNumber = "TRANSFER-OUT",
            Notes = $"Transfer to {targetWarehouseId}: {notes}",
            CreatedAt = utcNow
        });

        var movementIn = new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = targetWarehouseId,
            ToWarehouseId = sourceWarehouseId,
            ProductId = variant.ProductId,
            ProductVariantId = variant.Id,
            MovementType = StockMovementType.TransferIn,
            QuantityChange = quantity,
            QuantityAfter = targetStock.Quantity,
            AvailableAfter = targetStock.AvailableQuantity,
            ReservedAfter = targetStock.ReservedQuantity,
            DamagedAfter = targetStock.DamagedQuantity,
            ReferenceId = sourceWarehouseId,
            ReferenceNumber = "TRANSFER-IN",
            Notes = $"Transfer from {sourceWarehouseId}: {notes}",
            CreatedAt = utcNow
        };

        _context.StockMovements.Add(movementIn);

        _logger.LogInformation(
            "Stock transfer staged with locked ProductVariantStocks | TenantId: {TenantId} | Source: {SourceWarehouseId} | Target: {TargetWarehouseId} | VariantId: {VariantId} | Quantity: {Quantity}",
            tenantId,
            sourceWarehouseId,
            targetWarehouseId,
            variant.Id,
            quantity);

        return movementIn;
    }

    /// <summary>
    /// PHASE 11.5: Get stock levels using ProductVariantStocks (variant-centric).
    /// Inventory is Variant + Warehouse ONLY. Products never hold stock directly.
    /// </summary>
    public async Task<IEnumerable<ProductVariantStock>> GetStockLevelsAsync(Guid warehouseId)
    {
        // PHASE 11.5: Query ProductVariantStocks (source of truth)
        // NOT WarehouseStock (legacy)
        return await _context.ProductVariantStocks
            .Include(pvs => pvs.Variant)
                .ThenInclude(v => v.Product)
            .Include(pvs => pvs.Warehouse)
            .Where(pvs => pvs.WarehouseId == warehouseId && pvs.Variant.IsActive && !pvs.Variant.IsDeleted)
            .OrderBy(pvs => pvs.Variant.Product.Name)
            .ThenBy(pvs => pvs.Variant.IsDefault ? 0 : 1)
            .ToListAsync();
    }

    public async Task<OpeningStockSaveResult> SaveOpeningStockAsync(SaveOpeningStockRequest request)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            if (request.Items.Count == 0)
            {
                throw new InvalidOperationException("At least one opening stock item is required");
            }

            if (request.Items.Any(item => item.Quantity <= 0m))
            {
                throw new InvalidOperationException("Opening stock quantities must be positive");
            }

            var duplicateVariantId = request.Items
                .GroupBy(item => item.VariantId)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (duplicateVariantId.HasValue)
            {
                throw new InvalidOperationException(
                    $"Each variant may appear only once in an opening stock request. Duplicate variant: {duplicateVariantId.Value}");
            }

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == tenantId);

            if (warehouse == null || !warehouse.IsActive)
            {
                throw new InvalidOperationException("Warehouse not found or inactive");
            }

            var results = new List<OpeningStockItemResult>();
            var pendingChanges = new List<PendingProductVariantStockChange>();

            foreach (var item in request.Items)
            {
                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v =>
                        v.Id == item.VariantId &&
                        v.TenantId == tenantId &&
                        v.Product.TenantId == tenantId &&
                        v.IsActive &&
                        !v.IsDeleted);

                if (variant == null)
                {
                    results.Add(new OpeningStockItemResult(item.VariantId, false, "Variant not found or inactive"));
                    continue;
                }

                var existingStock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                    _context,
                    tenantId,
                    request.WarehouseId,
                    item.VariantId,
                    createIfMissing: true);

                if (existingStock == null)
                {
                    throw new InvalidOperationException(
                        $"Unable to materialize stock for variant {item.VariantId}.");
                }

                var hasPriorInventoryAuthority = existingStock.Quantity != 0m ||
                                                 existingStock.ReservedQuantity != 0m ||
                                                 existingStock.DamagedQuantity != 0m ||
                                                 await _context.StockMovements
                                                     .IgnoreQueryFilters()
                                                     .AnyAsync(movement =>
                                                         movement.TenantId == tenantId &&
                                                         movement.WarehouseId == request.WarehouseId &&
                                                         movement.ProductVariantId == item.VariantId);

                if (hasPriorInventoryAuthority)
                {
                    results.Add(new OpeningStockItemResult(
                        item.VariantId,
                        false,
                        "Opening stock is allowed only before any inventory authority exists for this variant and warehouse"));
                    continue;
                }

                var oldQuantity = existingStock.Quantity;
                existingStock.Quantity = item.Quantity;
                existingStock.UpdatedAt = DateTime.UtcNow;

                var stockMovement = new StockMovement
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    WarehouseId = request.WarehouseId,
                    ProductId = variant.ProductId,
                    ProductVariantId = item.VariantId,
                    MovementType = StockMovementType.OpeningBalance,
                    IsOpeningBalance = true,
                    QuantityChange = item.Quantity,
                    QuantityAfter = existingStock.Quantity,
                    Price = item.CostPrice,
                    ReferenceNumber = $"OB-VAR-{item.VariantId.ToString().Substring(0, 8)}-{request.WarehouseId.ToString().Substring(0, 8)}",
                    IdempotencyKey = $"opening-stock:{tenantId:N}:{request.WarehouseId:N}:{item.VariantId:N}",
                    Source = "WarehouseOpeningStock",
                    Notes = item.Notes ?? "Opening Stock Balance (Variant)",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.StockMovements.Add(stockMovement);
                pendingChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    item.VariantId,
                    variant.ProductId,
                    request.WarehouseId,
                    oldQuantity,
                    existingStock.Quantity,
                    "Opening Balance",
                    referenceId: stockMovement.Id,
                    referenceType: nameof(StockMovement),
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));

                results.Add(new OpeningStockItemResult(item.VariantId, true, "Opening stock saved successfully"));
            }

            await _context.SaveChangesAsync();

            if (pendingChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    tenantId,
                    pendingChanges);
            }

            await transaction.CommitAsync();

            var successCount = results.Count(result => result.Success);
            return new OpeningStockSaveResult(
                $"Opening stock processed: {successCount} successful",
                results);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid warehouseId, Guid? productId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.StockMovements
            .Include(sm => sm.Product)
            .Include(sm => sm.ProductVariant)
            .Where(sm => sm.WarehouseId == warehouseId);

        if (productId.HasValue)
        {
            query = query.Where(sm => sm.ProductId == productId);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt <= toDate.Value);
        }

        return await query.OrderByDescending(sm => sm.CreatedAt).ToListAsync();
    }
}
