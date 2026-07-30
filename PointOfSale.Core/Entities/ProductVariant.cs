using System;
using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Product Variant entity - the sellable SKU unit.
/// 
/// PHASE 11.1 — SHOPIFY-GRADE PRODUCT VARIANTS
/// 
/// Architecture:
/// - Product = catalog entity (display, SEO, category)
/// - ProductVariant = sellable SKU (price, stock, barcode)
/// 
/// Every Product has at least ONE default variant (IsDefault = true).
/// For simple products, this is the only variant.
/// For variable products (e.g., T-Shirt with sizes), multiple variants exist.
/// 
/// PRICING AUTHORITY:
/// - ProductVariant.Price, OnlinePrice, CostPrice are canonical for variant products
/// - Product.SalePrice, OnlinePrice, PurchasePrice are legacy compatibility mirrors
/// - Orders and carts continue using ProductId for now
/// - Will migrate to VariantId in Phase 11.2+
/// </summary>
public class ProductVariant : BaseEntity, ITenantOwned, IProjectionVersioned
{
    /// <summary>
    /// The tenant that owns this variant.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The parent product this variant belongs to.
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Stock Keeping Unit - unique identifier for this variant.
    /// For simple products, this equals Product.SKU.
    /// For variable products, includes attribute suffix (e.g., "SKU-RED-XL").
    /// </summary>
    public string? SKU { get; set; }
    
    /// <summary>
    /// Variant-specific barcode.
    /// For simple products, this equals Product.Barcode.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Canonical fingerprint for the variant attribute combination within a product.
    /// PostgreSQL owns uniqueness for non-deleted variants by TenantId + ProductId + this key.
    /// </summary>
    public string? AttributeCombinationKey { get; set; }
    
    /// <summary>
    /// POS sale price for this variant.
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Online/Storefront price for e-commerce.
    /// If null or 0, falls back to Price.
    /// </summary>
    public decimal? OnlinePrice { get; set; }
    
    /// <summary>
    /// Purchase/cost price for this variant.
    /// </summary>
    public decimal CostPrice { get; set; }
    
    /// <summary>
    /// Whether this is the default variant for the product.
    /// Every product MUST have exactly ONE default variant.
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Whether this variant is active and can be sold.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// SQL-authoritative semantic ownership state. Only Current variants may own public
    /// purchasability, stock reservation, SKU/barcode ownership, channel mappings, or feed identity.
    /// </summary>
    public ProductVariantSemanticLifecycleState SemanticLifecycleState { get; set; } =
        ProductVariantSemanticLifecycleState.Current;

    /// <summary>
    /// Replacement variant after a merge. Null for current owners and split parent lineage.
    /// </summary>
    public Guid? SemanticReplacementVariantId { get; set; }

    /// <summary>
    /// Root lineage variant used to collapse replay/rebuild after merge or split.
    /// </summary>
    public Guid? SemanticLineageRootVariantId { get; set; }

    /// <summary>
    /// Idempotency key for the authoritative merge/split operation that last changed semantic state.
    /// </summary>
    public Guid? SemanticOperationId { get; set; }

    /// <summary>
    /// Reviewed operation kind such as variant-merge or variant-split.
    /// </summary>
    public string? SemanticOperationKind { get; set; }

    /// <summary>
    /// Timestamp when the variant stopped being the current semantic owner.
    /// </summary>
    public DateTime? SemanticSupersededAtUtc { get; set; }
    
    /// <summary>
    /// Selected product media image for this variant.
    /// Preferred over ImageUrl when present.
    /// </summary>
    public Guid? MediaId { get; set; }
    
    /// <summary>
    /// Variant-specific image URL (optional).
    /// Falls back to product images if not set.
    /// </summary>
    public string? ImageUrl { get; set; }
    
    /// <summary>
    /// Display order for variant selection UI.
    /// </summary>
    public int SortOrder { get; set; }
    
    private decimal? _weight;
    
    /// <summary>
    /// Variant-specific weight in kg for shipping calculations.
    /// If null, falls back to Product.Weight.
    /// Used for weight-based shipping quotes when variant has different weight than product.
    /// Must be non-negative when set.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Weight must be non-negative")]
    public decimal? Weight
    {
        get => _weight;
        set
        {
            if (value.HasValue && value.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Weight), value, "Weight must be non-negative");
            }
            _weight = value;
        }
    }

    /// <summary>
    /// Monotonic stock mutation timestamp used to reject stale read-model updates.
    /// </summary>
    public DateTime? StockUpdatedAtUtc { get; set; }

    /// <summary>
    /// Monotonic source version used by downstream read models to reject stale variant projections.
    /// </summary>
    public long ProjectionMutationVersion { get; private set; }

    /// <summary>
    /// PostgreSQL xmin-backed optimistic concurrency token.
    /// Prevents stale concurrent writers from silently overwriting newer committed pricing truth.
    /// </summary>
    public uint RowVersion { get; set; }
    
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
    
    /// <summary>
    /// Computed effective price for storefront.
    /// Uses OnlinePrice if set, otherwise falls back to Price.
    /// </summary>
    public decimal EffectiveOnlinePrice => OnlinePrice.HasValue && OnlinePrice.Value > 0 
        ? OnlinePrice.Value 
        : Price;
    
    // Navigation properties
    public Product Product { get; set; } = null!;
    public ProductMedia? Media { get; set; }
    public ICollection<ProductVariantAttribute> Attributes { get; set; } = new List<ProductVariantAttribute>();
    public ICollection<ProductVariantStock> Stocks { get; set; } = new List<ProductVariantStock>();
}
