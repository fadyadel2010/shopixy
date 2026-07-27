namespace PointOfSale.Core.Events;

#region Product Events

/// <summary>
/// Embedded unit pricing data for product events.
/// Contains the selling price for a specific unit of measure.
/// </summary>
public class ProductUnitData
{
    public Guid UnitId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public decimal SalePrice { get; set; }
    public decimal ConversionFactor { get; set; } = 1;
    public bool IsBaseUnit { get; set; }
}

/// <summary>
/// Structured FAQ item included in product integration events.
/// </summary>
public class ProductFaqData
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a new product is created
/// </summary>
public class ProductCreatedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    
    /// <summary>
    /// LEGACY: Sale price from Products table.
    /// Used for POS system.
    /// </summary>
    public decimal SalePrice { get; set; }
    
    /// <summary>
    /// Online/Storefront price for e-commerce display.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// If null or 0, falls back to SalePrice.
    /// </summary>
    public decimal? OnlinePrice { get; set; }
    
    public decimal PurchasePrice { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    
    /// <summary>
    /// Brand name for filtering and display in product discovery.
    /// </summary>
    public string? Brand { get; set; }
    
    // Required for E-commerce Product Detail Page (MongoDB)
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public List<ProductFaqData> Faqs { get; set; } = new();
    public bool AiGenerated { get; set; }
    public string? AiMetadataJson { get; set; }
    
    // JSON or CSV string of image URLs - used for gallery
    public string? Images { get; set; }
    
    public bool IsOnlineVisible { get; set; }

    /// <summary>
    /// Whether this product is eligible for storefront search and discovery.
    /// </summary>
    public bool IsSearchable { get; set; } = true;

    /// <summary>
    /// Whether this product is curated for featured storefront surfaces.
    /// </summary>
    public bool IsFeatured { get; set; }
    
    // SEO Friendly URL slug
    public string? Slug { get; set; }

    /// <summary>
    /// Optional product page template identifier for the storefront.
    /// Null means the default template should be used.
    /// </summary>
    public string? ProductTemplate { get; set; }
    
    /// <summary>
    /// Default selling unit for this product.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// If null, product cannot be purchased online.
    /// </summary>
    public ProductUnitData? DefaultUnit { get; set; }
    
    /// <summary>
    /// All available units for this product.
    /// Used for multi-unit support.
    /// </summary>
    public List<ProductUnitData> Units { get; set; } = new();
    
    /// <summary>
    /// Whether to track inventory for this product.
    /// If false, stock validation is skipped during checkout.
    /// </summary>
    public bool TrackStock { get; set; } = true;
    
    /// <summary>
    /// Whether to allow backorders for this product.
    /// If true, product can be purchased even when out of stock.
    /// </summary>
    public bool AllowBackorder { get; set; }
    
    /// <summary>
    /// Whether this product has multiple variants requiring variant selection.
    /// PHASE 11.6: Products with HasVariants = true MUST have variantId in cart requests.
    /// </summary>
    public bool HasVariants { get; set; }
    
    /// <summary>
    /// Product weight in kg for shipping calculations.
    /// </summary>
    public decimal Weight { get; set; }
}

/// <summary>
/// Event raised when a product is updated
/// </summary>
public class ProductUpdatedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string[] ChangedFields { get; set; } = Array.Empty<string>();
    
    // Include all fields that might have changed
    public string? Name { get; set; }
    public string? NameAr { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal? SalePrice { get; set; }
    
    /// <summary>
    /// Online/Storefront price for e-commerce display.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// </summary>
    public decimal? OnlinePrice { get; set; }
    
    public decimal? PurchasePrice { get; set; }
    public Guid? CategoryId { get; set; }
    
    /// <summary>
    /// Brand name for filtering and display in product discovery.
    /// </summary>
    public string? Brand { get; set; }
    
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Features { get; set; }
    public List<ProductFaqData>? Faqs { get; set; }
    public bool? AiGenerated { get; set; }
    public string? AiMetadataJson { get; set; }
    public string? Images { get; set; }
    public bool? IsOnlineVisible { get; set; }
    public bool? IsSearchable { get; set; }
    public bool? IsFeatured { get; set; }
    public string? Slug { get; set; }
    public string? ProductTemplate { get; set; }
    public string? PublishStatus { get; set; }
    
    /// <summary>
    /// Default selling unit for this product (updated when units change).
    /// </summary>
    public ProductUnitData? DefaultUnit { get; set; }
    
    /// <summary>
    /// All available units for this product.
    /// </summary>
    public List<ProductUnitData> Units { get; set; } = new();
    
    /// <summary>
    /// Whether to track inventory for this product.
    /// </summary>
    public bool? TrackStock { get; set; }
    
    /// <summary>
    /// Whether to allow backorders for this product.
    /// </summary>
    public bool? AllowBackorder { get; set; }
    
    /// <summary>
    /// Whether this product has multiple variants requiring variant selection.
    /// PHASE 11.6: Products with HasVariants = true MUST have variantId in cart requests.
    /// </summary>
    public bool? HasVariants { get; set; }
    
    /// <summary>
    /// Product weight in kg for shipping calculations.
    /// </summary>
    public decimal? Weight { get; set; }
}

/// <summary>
/// Event raised when a product is deleted (soft delete)
/// </summary>
public class ProductDeletedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
}

/// <summary>
/// Event raised when a product's price changes
/// </summary>
public class ProductPriceChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
}

/// <summary>
/// Event raised when product stock level changes
/// </summary>
public class ProductStockChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    
    /// <summary>
    /// Total stock quantity across all warehouses.
    /// Used for MongoDB ProductDocument.StockQuantity sync.
    /// </summary>
    public int StockQuantity { get; set; }
    
    /// <summary>
    /// Whether to track inventory for this product.
    /// </summary>
    public bool TrackStock { get; set; } = true;
    
    /// <summary>
    /// Whether to allow backorders for this product.
    /// </summary>
    public bool AllowBackorder { get; set; }
}

/// <summary>
/// Event raised when product stock falls below threshold
/// </summary>
public class LowStockAlertEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public string? ProductName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal CurrentStock { get; set; }
    public int Threshold { get; set; }
}

/// <summary>
/// Event raised when product publish status changes
/// </summary>
public class ProductPublishStatusChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public bool IsOnlineVisible { get; set; }
}

#endregion
