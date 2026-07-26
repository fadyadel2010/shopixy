using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Validates that refund completion does not exceed sub-order or payment totals.
/// Single-sourced ceiling logic for CommerceHubSubOrder, CommerceHubPayment, and CommerceHubRefund.
/// </summary>
public sealed class RefundCompletionValidator : IRefundCompletionValidator
{
    private readonly ApplicationDbContext _dbContext;

    public RefundCompletionValidator(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ValidateRefundDoesNotExceedTotalsAsync(
        CommerceHubSubOrder subOrder,
        CommerceHubPayment? payment,
        CommerceHubRefund trackedRefund,
        CancellationToken cancellationToken)
    {
        var committedOrReservedSubOrderRefunds = await _dbContext.CommerceHubRefunds
            .Where(refund =>
                refund.SubOrderId == subOrder.Id &&
                refund.Id != trackedRefund.Id &&
                refund.Status != CommerceHubRefundStatus.Failed &&
                !refund.IsDeleted)
            .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

        if (RoundCurrency(committedOrReservedSubOrderRefunds + trackedRefund.Amount) > RoundCurrency(subOrder.Total))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_COMPLETION_LIMIT_EXCEEDED",
                "Refund completion would exceed the sub-order financial total. Reconcile the existing refund reservations before completing this refund.",
                StatusCodes.Status409Conflict,
                new
                {
                    subOrderId = subOrder.Id,
                    refundId = trackedRefund.Id,
                    requestedAmount = trackedRefund.Amount,
                    alreadyReservedOrCompletedAmount = committedOrReservedSubOrderRefunds,
                    subOrderTotal = subOrder.Total
                });
        }

        if (payment == null)
        {
            return;
        }

        var committedOrReservedPaymentRefunds = await _dbContext.CommerceHubRefunds
            .Where(refund =>
                refund.PaymentId == payment.Id &&
                refund.Id != trackedRefund.Id &&
                refund.Status != CommerceHubRefundStatus.Failed &&
                !refund.IsDeleted)
            .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;

        if (RoundCurrency(committedOrReservedPaymentRefunds + trackedRefund.Amount) > RoundCurrency(payment.Amount))
        {
            throw new CommerceHubCheckoutException(
                "REFUND_PAYMENT_COMPLETION_LIMIT_EXCEEDED",
                "Refund completion would exceed the captured payment amount. Reconcile the existing refund reservations before completing this refund.",
                StatusCodes.Status409Conflict,
                new
                {
                    paymentId = payment.Id,
                    refundId = trackedRefund.Id,
                    requestedAmount = trackedRefund.Amount,
                    alreadyReservedOrCompletedAmount = committedOrReservedPaymentRefunds,
                    paymentAmount = payment.Amount
                });
        }
    }

    private static decimal RoundCurrency(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
