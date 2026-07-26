using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Product entity with pricing, stock, tax configuration, and e-commerce fields
/// </summary>
public class Product : BaseEntity, ITenantOwned, IProjectionVersioned
{
    /// <summary>
    /// The tenant that owns this product.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Arabic name for bilingual support
    /// </summary>
    public string? NameAr { get; set; }
    
    public string? Barcode { get; set; }
    public string? SKU { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Arabic description for bilingual support
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Structured product features stored as JSON array.
    /// Marketplace experiences must consume this instead of parsing Description.
    /// </summary>
    public string? FeaturesJson { get; set; }

    /// <summary>
    /// Structured product FAQs stored as JSON array of { question, answer } objects.
    /// Marketplace experiences must consume this instead of parsing Description.
    /// </summary>
    public string? FaqJson { get; set; }
    
    /// <summary>
    /// Category identifier. MUST NOT be Guid.Empty to prevent category-scoped coupon bypass.
    /// SECURITY: Empty category IDs are filtered out in coupon validation, allowing products
    /// to bypass category restrictions. This is enforced at entity, DTO, and database levels.
    /// </summary>
    public Guid CategoryId { get; set; }
    
    /// <summary>
    /// Purchase/cost price
    /// </summary>
    public decimal PurchasePrice { get; set; }
    
    /// <summary>
    /// Sale price (can be tax-inclusive or exclusive based on PriceIncludesTax)
    /// Used for POS system.
    /// </summary>
    public decimal SalePrice { get; set; }
    
    /// <summary>
    /// Online/Storefront price for e-commerce display.
    /// If null or 0, falls back to SalePrice.
    /// </summary>
    public decimal? OnlinePrice { get; set; }
    
    /// <summary>
    /// Whether the sale price includes tax
    /// </summary>
    public bool PriceIncludesTax { get; set; }
    
    /// <summary>
    /// Tax rate percentage (e.g., 15 for 15%)
    /// </summary>
    public decimal TaxRate { get; set; }
    
    /// <summary>
    /// Unit of measure (e.g., "Piece", "Kg", "Liter")
    /// </summary>
    public string Unit { get; set; } = "Piece";
    
    /// <summary>
    /// Minimum stock level for alerts
    /// </summary>
    public decimal MinimumStockLevel { get; set; }
    
    /// <summary>
    /// Product images (comma-separated URLs or paths)
    /// </summary>
    public string? Images { get; set; }
    
    public bool IsActive { get; set; } = true;
    public bool TrackStock { get; set; } = true;
    
    /// <summary>
    /// PHASE 11.5 — SHOPIFY-GRADE PRODUCT AUTHORING
    /// 
    /// When FALSE (Simple Product Mode):
    /// - Product has exactly ONE default variant
    /// - UI shows product-level SKU, Barcode, Price, OnlinePrice, Cost, Stock
    /// - All commerce fields are actually stored on the default variant
    /// - User never sees the word "Variant"
    /// 
    /// When TRUE (Variant Product Mode):
    /// - Product has MULTIPLE variants (e.g., sizes, colors)
    /// - Product.Price, OnlinePrice, Barcode must be NULL (enforced)
    /// - All commerce fields are managed per-variant
    /// - ProductVariantsTab shows full variant grid
    /// </summary>
    public bool HasVariants { get; set; } = false;
    
    public string? Notes { get; set; }
    
    #region E-commerce SEO Fields
    
    /// <summary>
    /// URL-friendly slug for product page (e.g., "blue-widget-xl")
    /// </summary>
    public string? Slug { get; set; }
    
    /// <summary>
    /// SEO meta title
    /// </summary>
    public string? MetaTitle { get; set; }
    
    /// <summary>
    /// SEO meta description
    /// </summary>
    public string? MetaDescription { get; set; }
    
    /// <summary>
    /// SEO keywords (comma-separated)
    /// </summary>
    public string? MetaKeywords { get; set; }
    
    #endregion
    
    #region E-commerce Shipping Fields
    
    /// <summary>
    /// Product weight for shipping calculation
    /// </summary>
    public decimal Weight { get; set; }
    
    /// <summary>
    /// Package length
    /// </summary>
    public decimal Length { get; set; }
    
    /// <summary>
    /// Package width
    /// </summary>
    public decimal Width { get; set; }
    
    /// <summary>
    /// Package height
    /// </summary>
    public decimal Height { get; set; }
    
    /// <summary>
    /// Weight unit: kg or lb
    /// </summary>
    public string WeightUnit { get; set; } = "kg";
    
    /// <summary>
    /// Dimension unit: cm or in
    /// </summary>
    public string DimensionUnit { get; set; } = "cm";
    
    #endregion
    
    #region E-commerce Publication Fields
    
    /// <summary>
    /// Publication status: Draft, Published, Archived
    /// </summary>
    public PublishStatus PublishStatus { get; set; } = PublishStatus.Draft;
    
    /// <summary>
    /// Whether visible on e-commerce storefront
    /// </summary>
    public bool IsOnlineVisible { get; set; } = false;
    
    /// <summary>
    /// Whether included in search results
    /// </summary>
    public bool IsSearchable { get; set; } = true;
    
    /// <summary>
    /// Whether featured on homepage
    /// </summary>
    public bool IsFeatured { get; set; } = false;
    
    /// <summary>
    /// Low stock threshold for e-commerce alerts
    /// </summary>
    public int LowStockThreshold { get; set; } = 10;
    
    /// <summary>
    /// Allow backorders when out of stock
    /// </summary>
    public bool AllowBackorder { get; set; } = false;

    /// <summary>
    /// Monotonic source version used by downstream read models to reject stale projections.
    /// Incremented by the DbContext whenever this product aggregate changes.
    /// </summary>
    public long ProjectionMutationVersion { get; private set; }
    
    /// <summary>
    /// Advances the projection mutation version. Used by the persistence pipeline only.
    /// Enforces monotonicity - new version must be greater than current version.
    /// </summary>
    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "Projection mutation versions must be monotonically increasing.");
        }
        ProjectionMutationVersion = newVersion;
    }
    
    #endregion
    
    #region E-commerce Additional Fields
    
    /// <summary>
    /// Brand name
    /// </summary>
    public string? Brand { get; set; }
    
    /// <summary>
    /// Manufacturer
    /// </summary>
    public string? Manufacturer { get; set; }
    
    /// <summary>
    /// Tags for filtering (comma-separated: "bestseller,new,sale")
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Structured tag list stored as JSON array for marketplace consumption.
    /// </summary>
    public string? TagsJson { get; set; }

    /// <summary>
    /// Indicates whether the rich product content originated from AI generation.
    /// </summary>
    public bool AiGenerated { get; set; }

    /// <summary>
    /// Optional AI generation metadata stored as JSON.
    /// </summary>
    public string? AiMetadataJson { get; set; }
    
    /// <summary>
    /// Related product IDs (JSON array)
    /// </summary>
    public string? RelatedProductIds { get; set; }
    
    /// <summary>
    /// Video URL for product demonstration
    /// </summary>
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Optional storefront product page template identifier.
    /// Null means the default product template should be used.
    /// </summary>
    public string? ProductTemplate { get; set; }
    
    /// <summary>
    /// Amazon SP-API ASIN for syncing
    /// </summary>
    public string? AmazonASIN { get; set; }
    
    /// <summary>
    /// Last time product was synced with external systems
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
    
    #endregion
    
    // Navigation properties
    public Category Category { get; set; } = null!;
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductMedia> Media { get; set; } = new List<ProductMedia>();
    public ICollection<WarehouseStock> WarehouseStocks { get; set; } = new List<WarehouseStock>();
    public ICollection<ProductUnit> ProductUnits { get; set; } = new List<ProductUnit>();
}
