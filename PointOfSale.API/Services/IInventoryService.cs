using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Services;

public interface IInventoryService
{
    Task<StockMovement> AdjustStockAsync(Guid warehouseId, Guid productId, decimal quantityChange, StockMovementType type, string reason, Guid? productVariantId = null, Guid? referenceId = null, string? referenceNumber = null);
    Task<StockMovement> TransferStockAsync(Guid sourceWarehouseId, Guid targetWarehouseId, Guid productId, decimal quantity, string notes, Guid? productVariantId = null);
    /// <summary>
    /// PHASE 11.5: Get stock levels from ProductVariantStocks (variant-centric).
    /// Inventory is Variant + Warehouse ONLY.
    /// </summary>
    Task<IEnumerable<ProductVariantStock>> GetStockLevelsAsync(Guid warehouseId);
    Task<OpeningStockSaveResult> SaveOpeningStockAsync(SaveOpeningStockRequest request);
    Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid warehouseId, Guid? productId = null, DateTime? fromDate = null, DateTime? toDate = null);
}
