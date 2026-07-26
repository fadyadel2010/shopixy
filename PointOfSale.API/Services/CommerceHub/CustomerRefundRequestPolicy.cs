using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

internal readonly record struct CustomerRefundEligibility(
    int MaxRefundableQuantity,
    bool CanRefund,
    string? DisabledReason);

internal static class CustomerRefundRequestPolicy
{
    public const string ItemNotDeliveredDisabledReason = "Refund not available: Item not delivered";
    public const string RefundWindowExpiredDisabledReason = "Refund window expired";
    public const string MaximumRefundableQuantityReachedDisabledReason = "Maximum refundable quantity reached";

    public static bool CountsAgainstAvailableQuantity(CommerceHubRefundRequestStatus status) =>
        status is not (CommerceHubRefundRequestStatus.Rejected or CommerceHubRefundRequestStatus.Cancelled);

    public static int CalculateReservedQuantity(IEnumerable<CommerceHubRefundRequest> refundRequests) =>
        refundRequests
            .Where(refundRequest => !refundRequest.IsDeleted && CountsAgainstAvailableQuantity(refundRequest.Status))
            .Sum(refundRequest => refundRequest.Quantity);

    public static CustomerRefundEligibility Evaluate(
        CommerceHubOrderItemStatus itemStatus,
        DateTime? deliveredAt,
        int purchasedQuantity,
        IEnumerable<CommerceHubRefundRequest> refundRequests,
        RefundSettings settings,
        DateTime nowUtc)
    {
        if (itemStatus != CommerceHubOrderItemStatus.Delivered)
        {
            return new CustomerRefundEligibility(0, false, ItemNotDeliveredDisabledReason);
        }

        if (!IsWithinRefundWindow(deliveredAt, settings, nowUtc))
        {
            return new CustomerRefundEligibility(0, false, RefundWindowExpiredDisabledReason);
        }

        var reservedQuantity = CalculateReservedQuantity(refundRequests);
        var maxRefundableQuantity = Math.Max(0, purchasedQuantity - reservedQuantity);

        if (maxRefundableQuantity <= 0)
        {
            return new CustomerRefundEligibility(
                0,
                false,
                MaximumRefundableQuantityReachedDisabledReason);
        }

        return new CustomerRefundEligibility(maxRefundableQuantity, true, null);
    }

    public static bool IsWithinRefundWindow(DateTime? deliveredAt, RefundSettings settings, DateTime nowUtc)
    {
        if (!deliveredAt.HasValue)
        {
            return false;
        }

        return deliveredAt.Value >= nowUtc.Subtract(GetRefundWindow(settings));
    }

    private static TimeSpan GetRefundWindow(RefundSettings settings)
    {
        var refundWindowDays = settings.RefundWindowDays > 0
            ? settings.RefundWindowDays
            : 7;

        return TimeSpan.FromDays(refundWindowDays);
    }
}
