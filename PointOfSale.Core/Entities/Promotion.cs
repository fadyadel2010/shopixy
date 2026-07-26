using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Validation;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Promotion entity for automatic discounts (Offers).
/// 
/// PROMOTIONS ENGINE ARCHITECTURE:
/// - Coupons are code-based promotions (existing system - UNCHANGED)
/// - Offers are auto-applied promotions (this entity)
/// - Both are treated as "promotions" by the PromotionEngine
/// 
/// KEY DIFFERENCES FROM COUPONS:
/// - No code required - applied automatically at checkout
/// - Scoped to Product or Category
/// - Priority-based conflict resolution
/// 
/// CRITICAL RULES:
/// - TenantId is MANDATORY (multi-tenant isolation)
/// - Server-side enforcement ONLY (no frontend trust)
/// - ONE promotion per cart (no stacking, for now)
/// </summary>
public class Promotion : BaseEntity, ITenantOwned, IProjectionVersioned
{
    /// <summary>
    /// The tenant that owns this promotion.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Human-readable name for the promotion (e.g., "Summer Sale 20% Off")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Internal description for admin reference
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Type of discount: Percentage or Fixed Amount
    /// </summary>
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    
    /// <summary>
    /// Discount value (percentage 0-100 or fixed amount)
    /// </summary>
    public decimal DiscountValue { get; set; }
    
    /// <summary>
    /// Maximum discount amount (cap for percentage discounts)
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }
    
    /// <summary>
    /// Scope of the promotion: Product, Category, or Cart (entire order)
    /// </summary>
    public PromotionScope Scope { get; set; } = PromotionScope.Product;
    
    /// <summary>
    /// The entity this promotion applies to (ProductId or CategoryId based on Scope)
    /// Null means applies to all items in scope (e.g., all products if Scope=Product)
    /// </summary>
    public Guid? AppliesToId { get; set; }
    
    /// <summary>
    /// Promotion validity start date
    /// </summary>
    public DateTime StartDate { get; set; }
    
    /// <summary>
    /// Promotion validity end date
    /// </summary>
    public DateTime EndDate { get; set; }
    
    /// <summary>
    /// Priority for conflict resolution (higher = applied first)
    /// When multiple promotions match, the highest priority wins
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Whether the promotion is currently active (admin toggle)
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Whether this promotion is exclusive (cannot combine with coupons)
    /// Future-proofing for stacking rules
    /// </summary>
    public bool IsExclusive { get; set; } = false;
    
    /// <summary>
    /// Minimum order amount to qualify for this promotion
    /// </summary>
    public decimal? MinOrderAmount { get; set; }
    
    /// <summary>
    /// Maximum number of times this promotion can be applied (null = unlimited)
    /// </summary>
    public int? MaxUsage { get; set; }
    
    /// <summary>
    /// Current number of times this promotion has been used
    /// </summary>
    public int CurrentUsage { get; set; } = 0;
    
    /// <summary>
    /// Banner style for category-scoped promotions.
    /// Options: gradient, solid, minimal, festive
    /// Default: gradient
    /// </summary>
    public string BannerStyle { get; set; } = "gradient";
    
    // =========================================================================
    // SMART ENHANCEMENT: Manual Merchandising Override
    // Phase: Shopify-Grade Promotion-Aware Search Enhancements
    // =========================================================================
    
    /// <summary>
    /// Manual boost override for merchandising control.
    /// When set (non-null), this value ADDS to the calculated promotion boost.
    /// 
    /// Use cases:
    /// - Campaign launches (force promotion to top)
    /// - Seasonal priorities (holiday specials)
    /// - A/B testing different boost levels
    /// 
    /// SAFETY:
    /// - Additive only (never replaces text relevance)
    /// - Capped at tenant's MaxTotalBoost
    /// - Synced to Elasticsearch via events
    /// 
    /// Default: null (use calculated boost only)
    /// Recommended range: 1-50
    /// </summary>
    public double? SearchBoostOverride { get; set; }

    /// <summary>
    /// Monotonic source version used by downstream storefront promotion projections
    /// to reject stale promotion replays deterministically.
    /// </summary>
    public long ProjectionMutationVersion { get; private set; }

    /// <summary>
    /// Backend-owned lifecycle concurrency token. PostgreSQL maps this to xmin.
    /// </summary>
    public uint RowVersion { get; set; }

    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "Promotion projection mutation versions must be monotonically increasing.");
        }

        ProjectionMutationVersion = newVersion;
    }
    
    #region Computed Properties
    
    /// <summary>
    /// Validate the promotion discount configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Promotion name is required.");
        }

        DiscountValidation.EnsurePromotionValid(
            DiscountType,
            DiscountValue,
            MinOrderAmount,
            MaxDiscountAmount,
            MaxUsage,
            StartDate,
            EndDate);
    }

    /// <summary>
    /// Check if the promotion is currently valid based on dates and status
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return false;
        }

        if (!DiscountValidation.TryValidatePromotion(
                DiscountType,
                DiscountValue,
                MinOrderAmount,
                MaxDiscountAmount,
                MaxUsage,
                StartDate,
                EndDate,
                out _))
        {
            return false;
        }

        if (!IsActive) return false;
        var now = DateTime.UtcNow;
        if (now < StartDate) return false;
        if (now > EndDate) return false;
        if (MaxUsage.HasValue && CurrentUsage >= MaxUsage.Value) return false;
        return true;
    }
    
    /// <summary>
    /// Check if promotion applies to a specific product
    /// </summary>
    public bool AppliesToProduct(Guid productId, Guid? categoryId)
    {
        return Scope switch
        {
            PromotionScope.Product => AppliesToId == null || AppliesToId == productId,
            PromotionScope.Category => AppliesToId == null || AppliesToId == categoryId,
            PromotionScope.Cart => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Calculate discount amount for a given item price
    /// </summary>
    public decimal CalculateDiscount(decimal itemPrice)
    {
        if (!IsValid()) return 0;
        
        decimal discount = DiscountType == DiscountType.Percentage
            ? itemPrice * (DiscountValue / 100)
            : DiscountValue;
        
        // Apply max discount cap
        if (MaxDiscountAmount.HasValue && discount > MaxDiscountAmount.Value)
        {
            discount = MaxDiscountAmount.Value;
        }

        return CurrencyHelper.Round(Math.Min(discount, itemPrice));
    }
    
    #endregion
}
