using PointOfSale.Core.Discounts;

namespace PointOfSale.Core.Interfaces;

public interface ICouponValidationService
{
    Task<CouponValidationDecision> ValidateCouponByCodeAsync(
        Guid tenantId,
        string couponCode,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default);

    Task<CouponValidationDecision> ValidateCouponByIdAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default);

    Task<CouponReservationDecision> ReserveCouponAsync(
        Guid tenantId,
        Guid couponId,
        string couponCode,
        Guid orderId,
        Guid? customerId,
        CouponValidationSnapshot snapshot,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CouponConfirmationDecision> ConfirmCouponUsageAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CouponReleaseDecision> ReleaseCouponReservationAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<int> GetTotalConfirmedUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);

    Task<int> GetCustomerConfirmedUsageCountAsync(
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<bool> CustomerHasCompletedOrdersAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
