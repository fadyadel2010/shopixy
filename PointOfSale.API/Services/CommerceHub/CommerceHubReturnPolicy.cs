using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

public static class CommerceHubReturnPolicy
{
    public static bool CanSchedulePickup(CommerceHubReturnStatus status) =>
        status == CommerceHubReturnStatus.Pending;

    public static bool CanMarkPickedUp(CommerceHubReturnStatus status) =>
        status == CommerceHubReturnStatus.PickupScheduled;

    public static bool CanMarkInTransit(CommerceHubReturnStatus status) =>
        status == CommerceHubReturnStatus.PickedUp;

    public static bool CanMarkDelivered(CommerceHubReturnStatus status) =>
        status == CommerceHubReturnStatus.InTransit;

    public static bool CanMarkFailed(CommerceHubReturnStatus status) =>
        !IsTerminal(status);

    public static bool CanConfirmReceipt(CommerceHubReturnStatus status) =>
        status == CommerceHubReturnStatus.DeliveredToVendor;

    public static IReadOnlyList<CommerceHubReturnStatus> GetAllowedOverrideStatuses(
        CommerceHubReturnStatus currentStatus,
        CommerceHubRefundRequestStatus refundStatus,
        RefundExecutionTrigger trigger)
    {
        if (currentStatus == CommerceHubReturnStatus.Confirmed)
        {
            return Array.Empty<CommerceHubReturnStatus>();
        }

        var allowed = new List<CommerceHubReturnStatus>
        {
            CommerceHubReturnStatus.Pending,
            CommerceHubReturnStatus.PickupScheduled,
            CommerceHubReturnStatus.PickedUp,
            CommerceHubReturnStatus.InTransit,
            CommerceHubReturnStatus.DeliveredToVendor
        };

        if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundStatus))
        {
            allowed = allowed
                .Where(status => IsExecutionTriggerReached(trigger, status))
                .ToList();
        }

        return allowed;
    }

    public static bool CanOverrideStatus(
        CommerceHubReturnStatus currentStatus,
        CommerceHubRefundRequestStatus refundStatus,
        RefundExecutionTrigger trigger) =>
        GetAllowedOverrideStatuses(currentStatus, refundStatus, trigger).Count > 0;

    public static bool CanOverrideStatusTo(
        CommerceHubReturnStatus currentStatus,
        CommerceHubReturnStatus targetStatus,
        CommerceHubRefundRequestStatus refundStatus,
        RefundExecutionTrigger trigger) =>
        GetAllowedOverrideStatuses(currentStatus, refundStatus, trigger).Contains(targetStatus);

    public static bool IsTerminal(CommerceHubReturnStatus status) =>
        status is CommerceHubReturnStatus.Confirmed or CommerceHubReturnStatus.Failed;

    public static bool IsExecutionTriggerReached(
        RefundExecutionTrigger trigger,
        CommerceHubReturnStatus status)
    {
        if (status == CommerceHubReturnStatus.Failed)
        {
            return false;
        }

        return trigger switch
        {
            RefundExecutionTrigger.OnPickup => status >= CommerceHubReturnStatus.PickedUp,
            RefundExecutionTrigger.OnDelivery => status >= CommerceHubReturnStatus.DeliveredToVendor,
            RefundExecutionTrigger.OnVendorConfirmation => status >= CommerceHubReturnStatus.Confirmed,
            _ => false
        };
    }

    public static bool CanExecuteRefund(
        RefundExecutionTrigger trigger,
        CommerceHubReturnStatus? status)
    {
        return !status.HasValue || IsExecutionTriggerReached(trigger, status.Value);
    }

    public static string DescribeExecutionTrigger(RefundExecutionTrigger trigger) =>
        trigger switch
        {
            RefundExecutionTrigger.OnPickup => "picked up by courier",
            RefundExecutionTrigger.OnDelivery => "delivered to vendor",
            RefundExecutionTrigger.OnVendorConfirmation => "confirmed by vendor",
            _ => "eligible"
        };
}
