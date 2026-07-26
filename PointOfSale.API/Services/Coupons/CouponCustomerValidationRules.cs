using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Coupons;

/// <summary>
/// Shared customer-bound coupon validation rules.
/// 
/// These rules are reused by storefront validation and checkout validation so
/// customer identity can never be skipped for coupon eligibility decisions.
/// </summary>
internal static class CouponCustomerValidationRules
{
    internal static CouponAuthenticatedCustomerResult RequireAuthenticatedCustomer(Guid? customerId)
    {
        if (!customerId.HasValue || customerId.Value == Guid.Empty)
        {
            return CouponAuthenticatedCustomerResult.Failure(
                CouponErrorCodes.AuthenticationRequired,
                "You must sign in to use coupons");
        }

        return CouponAuthenticatedCustomerResult.Success(customerId.Value);
    }

    internal static async Task<CouponCustomerRulesValidationResult> ValidateCustomerRulesAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        int? maxUsagePerCustomer,
        bool firstOrderOnly,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default)
    {
        var customerUsageCount = 0;

        if (maxUsagePerCustomer.HasValue)
        {
            customerUsageCount = await dbContext.CouponUsages
                .CountAsync(
                    cu => cu.TenantId == tenantId &&
                          cu.CouponId == couponId &&
                          cu.CustomerId == customerId &&
                          !cu.IsDeleted,
                    cancellationToken);

            if (customerUsageCount >= maxUsagePerCustomer.Value)
            {
                return CouponCustomerRulesValidationResult.Failure(
                    customerUsageCount,
                    CouponErrorCodes.CustomerMaxExceeded,
                    "You have already used this coupon the maximum number of times");
            }
        }

        if (firstOrderOnly)
        {
            if (await HasQualifyingFirstOrderHistoryAsync(dbContext, tenantId, customerId, cancellationToken))
            {
                return CouponCustomerRulesValidationResult.Failure(
                    customerUsageCount,
                    CouponErrorCodes.FirstOrderOnly,
                    "This coupon is only valid for first-time orders");
            }

            if (await HasActiveFirstOrderCouponReservationAsync(
                    dbContext,
                    tenantId,
                    couponId,
                    customerId,
                    excludeOrderId,
                    cancellationToken))
            {
                return CouponCustomerRulesValidationResult.Failure(
                    customerUsageCount,
                    CouponErrorCodes.FirstOrderOnly,
                    "This coupon is already reserved by another pending order.");
            }
        }

        return CouponCustomerRulesValidationResult.Success(customerUsageCount);
    }

    internal static Task<bool> HasQualifyingFirstOrderHistoryAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Orders.AnyAsync(
            o => o.TenantId == tenantId &&
                 (o.OnlineCustomerId == customerId || o.CustomerId == customerId) &&
                 o.Status != OrderStatus.Cancelled &&
                 o.Status != OrderStatus.Refunded &&
                 o.FulfillmentStatus != FulfillmentStatus.Returned &&
                 o.PaymentStatus == PaymentStatus.Collected,
            cancellationToken);
    }

    internal static Task<bool> HasActiveFirstOrderCouponReservationAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid couponId,
        Guid customerId,
        Guid? excludeOrderId = null,
        CancellationToken cancellationToken = default)
    {
        return dbContext.CouponReservations.AnyAsync(
            reservation =>
                reservation.TenantId == tenantId &&
                reservation.CouponId == couponId &&
                reservation.CustomerId == customerId &&
                reservation.Status == CouponReservationStatus.Reserved &&
                !reservation.IsDeleted &&
                (!excludeOrderId.HasValue || reservation.OrderId != excludeOrderId.Value),
            cancellationToken);
    }
}

internal readonly record struct CouponAuthenticatedCustomerResult(
    bool IsAuthenticated,
    Guid CustomerId,
    string? ErrorCode,
    string? ErrorMessage)
{
    internal static CouponAuthenticatedCustomerResult Success(Guid customerId) =>
        new(true, customerId, null, null);

    internal static CouponAuthenticatedCustomerResult Failure(string errorCode, string errorMessage) =>
        new(false, Guid.Empty, errorCode, errorMessage);
}

internal readonly record struct CouponCustomerRulesValidationResult(
    bool IsValid,
    int CustomerUsageCount,
    string? ErrorCode,
    string? ErrorMessage)
{
    internal static CouponCustomerRulesValidationResult Success(int customerUsageCount) =>
        new(true, customerUsageCount, null, null);

    internal static CouponCustomerRulesValidationResult Failure(
        int customerUsageCount,
        string errorCode,
        string errorMessage) =>
        new(false, customerUsageCount, errorCode, errorMessage);
}
