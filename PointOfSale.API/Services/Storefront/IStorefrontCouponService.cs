using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for Storefront Coupon operations.
/// 
/// ARCHITECTURAL PRINCIPLES:
/// - Coupons are validated STATELESS every time (no cache of validity)
/// - Coupons live in PostgreSQL ONLY (no MongoDB projection)
/// - Coupon logic is SERVER-SIDE ONLY (frontend never calculates discounts)
/// - Coupons do NOT mutate cart items (they are a pricing overlay)
/// - Coupons are tenant-scoped (cross-tenant rejection)
/// 
/// Validation Rules (in order):
/// 1. Coupon exists and matches tenant
/// 2. Coupon is active (IsActive flag)
/// 3. Coupon is within date range (StartDate → EndDate)
/// 4. Global usage limit not exceeded (MaxUsage)
/// 5. Per-customer usage not exceeded (MaxUsagePerCustomer)
/// 6. First-order-only rule (if applicable)
/// 7. Minimum order amount met
/// 8. Product/category restrictions met (if applicable)
/// </summary>
public interface IStorefrontCouponService
{
    /// <summary>
    /// Validates a coupon code against the current cart.
    /// Does NOT apply the coupon - only validates and calculates discount.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="code">Coupon code (will be normalized)</param>
    /// <param name="cart">Current cart snapshot for validation</param>
    /// <param name="customerId">Authenticated customer ID (required, enforced at compile time)</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with discount amount if valid</returns>
    Task<CouponValidationResult> ValidateCouponAsync(
        Guid tenantId,
        string code,
        CartDto cart,
        Guid customerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalidates an already-applied coupon against the current cart.
    /// Called on every cart read/mutation to ensure coupon is still valid.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="code">Applied coupon code</param>
    /// <param name="cart">Current cart snapshot</param>
    /// <param name="customerId">Authenticated customer ID (required, enforced at compile time)</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result (if invalid, coupon should be auto-removed)</returns>
    Task<CouponValidationResult> RevalidateCouponAsync(
        Guid tenantId,
        string code,
        CartDto cart,
        Guid customerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the discount amount for a given coupon and cart subtotal.
    /// Applies MaxDiscountAmount cap if applicable.
    /// </summary>
    /// <param name="couponId">Coupon ID</param>
    /// <param name="cartSubtotal">Cart subtotal before discount</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Calculated discount amount</returns>
    Task<decimal> CalculateDiscountAsync(
        Guid couponId,
        decimal cartSubtotal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a customer has any completed orders.
    /// Used for first-order-only coupon validation.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="customerId">Customer ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if customer has previous orders</returns>
    Task<bool> CustomerHasOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of times a customer has used a specific coupon.
    /// Used for per-customer usage limit validation.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="customerId">Customer ID</param>
    /// <param name="couponId">Coupon ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of times customer has used this coupon</returns>
    Task<int> GetCustomerCouponUsageAsync(
        Guid tenantId,
        Guid customerId,
        Guid couponId,
        CancellationToken cancellationToken = default);
}
