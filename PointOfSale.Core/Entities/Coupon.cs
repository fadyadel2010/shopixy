using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Validation;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Coupon/Promo code entity for e-commerce discounts
/// </summary>
public class Coupon : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this coupon.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// PostgreSQL xmin-backed optimistic concurrency token for admin lifecycle mutations.
    /// </summary>
    public uint RowVersion { get; set; }
    
    /// <summary>
    /// Unique coupon code (e.g., SAVE10, SUMMER2024)
    /// Use SetCode() method to update this property to ensure NormalizedCode stays in sync.
    /// </summary>
    public string Code { get; private set; } = string.Empty;
    
    /// <summary>
    /// Normalized (uppercase) version of Code for case-insensitive lookups.
    /// CRITICAL: Must be kept in sync with Code. Set via SetCode() method.
    /// Database has unique index on (TenantId, NormalizedCode).
    /// </summary>
    public string NormalizedCode { get; private set; } = string.Empty;
    
    /// <summary>
    /// Internal description of the coupon
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Type of discount: Percentage or Fixed Amount
    /// </summary>
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    
    /// <summary>
    /// Discount value (percentage or fixed amount based on DiscountType)
    /// </summary>
    public decimal DiscountValue { get; set; }
    
    /// <summary>
    /// Minimum order amount required to use this coupon
    /// </summary>
    public decimal? MinOrderAmount { get; set; }
    
    /// <summary>
    /// Maximum discount amount (cap for percentage discounts)
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }
    
    /// <summary>
    /// Maximum number of times this coupon can be used (null = unlimited)
    /// </summary>
    public int? MaxUsage { get; set; }
    
    /// <summary>
    /// Current number of times this coupon has been used
    /// </summary>
    public int CurrentUsage { get; set; } = 0;
    
    /// <summary>
    /// Maximum uses per customer (null = unlimited)
    /// </summary>
    public int? MaxUsagePerCustomer { get; set; }
    
    /// <summary>
    /// Coupon validity start date
    /// </summary>
    public DateTime? StartDate { get; set; }
    
    /// <summary>
    /// Coupon validity end date
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    /// <summary>
    /// Whether the coupon is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Only valid for first-time customers
    /// </summary>
    public bool FirstOrderOnly { get; set; } = false;
    
    /// <summary>
    /// Applicable product IDs (JSON array, null = all products)
    /// </summary>
    public string? ApplicableProductIds { get; set; }
    
    /// <summary>
    /// Applicable category IDs (JSON array, null = all categories)
    /// </summary>
    public string? ApplicableCategoryIds { get; set; }
    
    /// <summary>
    /// Excluded product IDs (JSON array)
    /// </summary>
    public string? ExcludedProductIds { get; set; }
    
    /// <summary>
    /// Set the coupon code and automatically update NormalizedCode.
    /// Use this method instead of directly setting Code to ensure consistency.
    /// </summary>
    public void SetCode(string code)
    {
        Code = code?.Trim().ToUpperInvariant() ?? string.Empty;
        NormalizedCode = Code;
    }
    
    /// <summary>
    /// Validate the coupon discount configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            throw new InvalidOperationException("Coupon code is required.");
        }

        DiscountValidation.EnsureCouponValid(
            DiscountType,
            DiscountValue,
            MinOrderAmount,
            MaxDiscountAmount,
            MaxUsage,
            MaxUsagePerCustomer,
            StartDate,
            EndDate);
    }

    /// <summary>
    /// Check if the coupon is currently valid
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            return false;
        }

        if (!DiscountValidation.TryValidateCoupon(
                DiscountType,
                DiscountValue,
                MinOrderAmount,
                MaxDiscountAmount,
                MaxUsage,
                MaxUsagePerCustomer,
                StartDate,
                EndDate,
                out _))
        {
            return false;
        }

        if (!IsActive) return false;
        if (StartDate.HasValue && DateTime.UtcNow < StartDate.Value) return false;
        if (EndDate.HasValue && DateTime.UtcNow > EndDate.Value) return false;
        if (MaxUsage.HasValue && CurrentUsage >= MaxUsage.Value) return false;
        return true;
    }
    
    /// <summary>
    /// Calculate discount amount for a given order total
    /// </summary>
    public decimal CalculateDiscount(decimal orderTotal)
    {
        if (!IsValid()) return 0;
        if (MinOrderAmount.HasValue && orderTotal < MinOrderAmount.Value) return 0;
        
        decimal discount = DiscountType == DiscountType.Percentage
            ? orderTotal * (DiscountValue / 100)
            : DiscountValue;
        
        // Apply maximum discount cap
        if (MaxDiscountAmount.HasValue && discount > MaxDiscountAmount.Value)
        {
            discount = MaxDiscountAmount.Value;
        }

        return CurrencyHelper.Round(Math.Min(discount, orderTotal));
    }
}
