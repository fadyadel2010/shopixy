namespace PointOfSale.Core.Events;

#region Product Variant Events - Phase 11.1

/// <summary>
/// Attribute data embedded in variant events.
/// </summary>
public class VariantAttributeData
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// Stock data embedded in variant events.
/// </summary>
public class VariantStockData
{
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
}

/// <summary>
/// Canonical mutation source values for ProductVariantStockMutatedEvent.
/// These identify the system boundary that produced the stock mutation.
/// </summary>
public static class MutationSources
{
    /// <summary>
    /// Stock mutation originated from POS checkout or manual inventory adjustment.
    /// </summary>
    public const string POS = "POS";
    
    /// <summary>
    /// Stock mutation originated from CommerceHub order processing.
    /// </summary>
    public const string CommerceHub = "CommerceHub";
}

/// <summary>
/// Event raised when a new product variant is created.
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// Emitted but NOT projected to Mongo yet (Phase 11.3).
/// </summary>
public class ProductVariantCreatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    /// <summary>
    /// The variant ID (primary key).
    /// </summary>
    public Guid VariantId { get; set; }
    
    /// <summary>
    /// The parent product ID.
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Product name (for denormalization).
    /// </summary>
    public string? ProductName { get; set; }
    
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    
    /// <summary>
    /// POS sale price.
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Online/Storefront price.
    /// </summary>
    public decimal? OnlinePrice { get; set; }
    
    /// <summary>
    /// Purchase/cost price.
    /// </summary>
    public decimal CostPrice { get; set; }
    
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    
    /// <summary>
    /// Display order for variant selection UI.
    /// </summary>
    public int? SortOrder { get; set; }
    
    /// <summary>
    /// Variant attributes (Color=Red, Size=XL, etc.).
    /// </summary>
    public List<VariantAttributeData> Attributes { get; set; } = new();

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("ProductVariantCreatedEvent must carry a non-empty VariantId.");
}

/// <summary>
/// Event raised when a product variant is updated.
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// Emitted but NOT projected to Mongo yet (Phase 11.3).
/// </summary>
public class ProductVariantUpdatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// List of field names that changed.
    /// </summary>
    public string[] ChangedFields { get; set; } = Array.Empty<string>();
    
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal? Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal? CostPrice { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsActive { get; set; }
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    
    /// <summary>
    /// Display order for variant selection UI.
    /// </summary>
    public int? SortOrder { get; set; }
    
    /// <summary>
    /// Updated variant attributes (if changed).
    /// </summary>
    public List<VariantAttributeData>? Attributes { get; set; }

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("ProductVariantUpdatedEvent must carry a non-empty VariantId.");
}

/// <summary>
/// Event raised when a variant's effective selling price changes.
/// Used by downstream projections that depend on fallback variant pricing.
/// </summary>
public class VariantPriceUpdatedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public decimal NewPrice { get; set; }

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("VariantPriceUpdatedEvent must carry a non-empty VariantId.");
}

/// <summary>
/// Event raised when product variant stock changes.
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// Emitted but NOT projected to Mongo yet (Phase 11.3).
/// </summary>
public class ProductVariantStockChangedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    
    /// <summary>
    /// Reason for stock change (e.g., "Sale", "Purchase", "Adjustment").
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Reference to the document that caused the change (OrderId, PurchaseInvoiceId, etc.).
    /// </summary>
    public Guid? ReferenceId { get; set; }
    
    /// <summary>
    /// Type of reference document.
    /// </summary>
    public string? ReferenceType { get; set; }

    /// <summary>
    /// System boundary that produced the stock mutation. Downstream consumers use
    /// this to reconcile POS checkout, CommerceHub, and manual inventory paths
    /// without treating them as different sources of truth.
    /// </summary>
    public string? MutationSource { get; set; }
    
    /// <summary>
    /// Total stock quantity across all warehouses for this variant.
    /// </summary>
    public decimal TotalStockQuantity { get; set; }

    /// <summary>
    /// Monotonic source-of-truth stock timestamp used by projections to reject stale updates.
    /// </summary>
    public DateTime StockUpdatedAtUtc { get; set; }

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("ProductVariantStockChangedEvent must carry a non-empty VariantId.");
}

/// <summary>
/// Event raised when a product variant is deleted (soft delete).
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// Emitted but NOT projected to Mongo yet (Phase 11.3).
/// </summary>
public class ProductVariantDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("ProductVariantDeletedEvent must carry a non-empty VariantId.");
}

#endregion
