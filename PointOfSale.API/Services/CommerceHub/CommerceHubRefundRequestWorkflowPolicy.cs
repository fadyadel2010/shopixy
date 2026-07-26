using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubRefundRequestWorkflowPolicy
{
    public static CommerceHubRefundRequestStatus NormalizeStatus(CommerceHubRefundRequestStatus status) =>
        status switch
        {
            CommerceHubRefundRequestStatus.Completed or CommerceHubRefundRequestStatus.Executed or CommerceHubRefundRequestStatus.Paid
                => CommerceHubRefundRequestStatus.Paid,
            CommerceHubRefundRequestStatus.ReadyForExecution or CommerceHubRefundRequestStatus.AwaitingPayout
                => CommerceHubRefundRequestStatus.AwaitingPayout,
            _ => status
        };

    public static bool IsPaid(CommerceHubRefundRequestStatus status) =>
        NormalizeStatus(status) == CommerceHubRefundRequestStatus.Paid;

    public static bool IsExecuted(CommerceHubRefundRequestStatus status) =>
        IsPaid(status);

    public static bool CanMaintainReturnWorkflow(CommerceHubRefundRequestStatus status) =>
        NormalizeStatus(status) is CommerceHubRefundRequestStatus.Approved or
            CommerceHubRefundRequestStatus.WaitingForReturn or
            CommerceHubRefundRequestStatus.AwaitingPayout or
            CommerceHubRefundRequestStatus.Processing;

    public static bool CanTransitionToAwaitingPayout(CommerceHubRefundRequestStatus status) =>
        NormalizeStatus(status) is CommerceHubRefundRequestStatus.Approved or CommerceHubRefundRequestStatus.WaitingForReturn;

    public static CommerceHubRefundRequestStatus ResolveUnlockedState(
        RefundExecutionTrigger trigger,
        CommerceHubReturnStatus? returnStatus)
    {
        return returnStatus.HasValue &&
               CommerceHubReturnPolicy.IsExecutionTriggerReached(trigger, returnStatus.Value)
            ? CommerceHubRefundRequestStatus.AwaitingPayout
            : CommerceHubRefundRequestStatus.WaitingForReturn;
    }

    public static bool CanExecute(CommerceHubRefundRequest refundRequest)
    {
        var normalizedStatus = NormalizeStatus(refundRequest.Status);
        if (IsPaid(normalizedStatus))
        {
            return false;
        }

        if (normalizedStatus is not (
                CommerceHubRefundRequestStatus.AwaitingPayout or
                CommerceHubRefundRequestStatus.Approved))
        {
            return false;
        }

        return true;
    }
}
