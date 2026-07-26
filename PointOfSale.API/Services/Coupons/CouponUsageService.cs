using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Coupons;

/// <summary>
/// Legacy adapter that forwards coupon validation to the unified SQL authority.
/// Coupon confirmation now happens through ICouponValidationService on paid transitions.
/// </summary>
public class CouponUsageService : ICouponUsageService
{
    private readonly ICouponValidationService _couponValidationService;

    public CouponUsageService(ICouponValidationService couponValidationService)
    {
        _couponValidationService = couponValidationService;
    }

    public async Task<CouponValidationForCheckoutResult> ValidateCouponForCheckoutAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid? customerId,
        decimal orderSubtotal,
        string correlationId,
        IReadOnlyCollection<CouponScopeValidationItem>? cartItems = null,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await _couponValidationService.ValidateCouponByIdAsync(
            tenantId,
            couponId,
            couponCode,
            customerId,
            new CouponValidationSnapshot
            {
                OrderSubtotal = orderSubtotal,
                CartItems = cartItems ?? Array.Empty<CouponScopeValidationItem>()
            },
            correlationId,
            excludeOrderId,
            cancellationToken);

        return decision.IsValid && decision.CouponId.HasValue
            ? CouponValidationForCheckoutResult.Success(
                decision.CouponId.Value,
                decision.CouponCode ?? couponCode,
                decision.DiscountType ?? "fixed",
                decision.DiscountValue ?? 0m,
                decision.CalculatedDiscountAmount ?? 0m,
                decision.ConfirmedUsageCount,
                decision.CustomerConfirmedUsageCount)
            : CouponValidationForCheckoutResult.Failure(
                decision.ErrorCode ?? CouponErrorCodes.Invalid,
                decision.ErrorMessage ?? "Coupon is invalid.");
    }

    [Obsolete("Early coupon consumption has been removed. Reserve during checkout and confirm after payment success.")]
    public Task<CouponUsage> ConsumeCouponAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid orderId,
        Guid? customerId,
        decimal discountAmount,
        decimal orderSubtotal,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        throw new CouponAuthorityException(
            "COUPON_EARLY_CONSUMPTION_REMOVED",
            "Coupons can no longer be consumed before payment success. Use ICouponValidationService reservation/confirmation lifecycle instead.");
    }

    public Task<int> GetTotalUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        return _couponValidationService.GetTotalConfirmedUsageCountAsync(tenantId, couponId, cancellationToken);
    }

    public Task<int> GetCustomerUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _couponValidationService.GetCustomerConfirmedUsageCountAsync(
            tenantId,
            couponId,
            customerId,
            cancellationToken);
    }

    public Task<bool> CustomerHasCompletedOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _couponValidationService.CustomerHasCompletedOrdersAsync(tenantId, customerId, cancellationToken);
    }
}
