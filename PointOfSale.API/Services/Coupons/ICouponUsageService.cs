using PointOfSale.Core.Entities;
using PointOfSale.Core.Discounts;

namespace PointOfSale.API.Services.Coupons;

/// <summary>
/// Legacy adapter interface retained for compatibility while the codebase
/// migrates to ICouponValidationService.
/// </summary>
public interface ICouponUsageService
{
    /// <summary>
    /// Validates a coupon for checkout, enforcing ALL constraints:
    /// - Authenticated customer identity is required
    /// - IsActive check
    /// - Date range (StartDate/EndDate) 
    /// - MaxUsage (total usage count)
    /// - MaxPerCustomer (per-customer usage count)
    /// - FirstOrderOnly (customer has no completed orders)
    /// - MinOrderAmount
    /// 
    /// CRITICAL: This MUST be called DURING checkout, NOT in cart preview.
    /// Uses CouponUsages table for accurate counts.
    /// </summary>
    /// <param name="excludeOrderId">Optional order ID to exclude from reservation conflict checks (used during replay/retry scenarios)</param>
    Task<CouponValidationForCheckoutResult> ValidateCouponForCheckoutAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid? customerId,
        decimal orderSubtotal,
        string correlationId,
        IReadOnlyCollection<CouponScopeValidationItem>? cartItems = null,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy API kept so old call sites fail closed instead of silently burning usage before payment.
    /// New code must reserve through ICouponValidationService during checkout and confirm after payment success.
    /// </summary>
    Task<CouponUsage> ConsumeCouponAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid orderId,
        Guid? customerId,
        decimal discountAmount,
        decimal orderSubtotal,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total usage count for a coupon.
    /// Used for MaxUsage validation and admin display.
    /// </summary>
    Task<int> GetTotalUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the usage count for a specific customer.
    /// Used for MaxPerCustomer validation.
    /// </summary>
    Task<int> GetCustomerUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a customer has any qualifying successful orders for first-order-only coupon rules.
    /// Used for FirstOrderOnly validation.
    /// </summary>
    Task<bool> CustomerHasCompletedOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of coupon validation for checkout.
/// </summary>
public class CouponValidationForCheckoutResult
{
    public bool IsValid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Coupon details (only populated if valid)
    public Guid? CouponId { get; set; }
    public string? CouponCode { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal? CalculatedDiscountAmount { get; set; }
    
    // Usage info (for logging/debugging)
    public int TotalUsageCount { get; set; }
    public int CustomerUsageCount { get; set; }

    public static CouponValidationForCheckoutResult Success(
        Guid couponId, string couponCode, string discountType, decimal discountValue, 
        decimal calculatedDiscount, int totalUsage, int customerUsage)
    {
        return new CouponValidationForCheckoutResult
        {
            IsValid = true,
            CouponId = couponId,
            CouponCode = couponCode,
            DiscountType = discountType,
            DiscountValue = discountValue,
            CalculatedDiscountAmount = calculatedDiscount,
            TotalUsageCount = totalUsage,
            CustomerUsageCount = customerUsage
        };
    }

    public static CouponValidationForCheckoutResult Failure(string errorCode, string errorMessage)
    {
        return new CouponValidationForCheckoutResult
        {
            IsValid = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
