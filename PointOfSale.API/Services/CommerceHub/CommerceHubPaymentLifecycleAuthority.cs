using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubPaymentLifecycleAuthority
{
    public static void MarkOnlinePaymentCollected(
        CommerceHubMarketplaceOrder marketplaceOrder,
        IEnumerable<CommerceHubSubOrder> affectedSubOrders,
        string? paymentReferenceId,
        DateTime nowUtc)
    {
        using var boundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        EnsureCanCollect(marketplaceOrder.PaymentStatus, nameof(CommerceHubMarketplaceOrder), marketplaceOrder.Id);

        marketplaceOrder.PaymentStatus = PaymentStatus.Collected;
        marketplaceOrder.PaymentReferenceId = paymentReferenceId;
        marketplaceOrder.PaidAt ??= nowUtc;

        foreach (var subOrder in affectedSubOrders)
        {
            EnsureCanCollect(subOrder.PaymentStatus, nameof(CommerceHubSubOrder), subOrder.Id);
            subOrder.PaymentStatus = PaymentStatus.Collected;
            subOrder.PaymentReferenceId = paymentReferenceId;
            subOrder.PaidAt ??= nowUtc;
        }
    }

    public static void ResetOnlinePaymentToPendingForRetry(
        CommerceHubMarketplaceOrder marketplaceOrder,
        IEnumerable<CommerceHubSubOrder> affectedSubOrders)
    {
        using var boundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        EnsureCanRetry(marketplaceOrder.PaymentStatus, nameof(CommerceHubMarketplaceOrder), marketplaceOrder.Id);

        marketplaceOrder.PaymentStatus = PaymentStatus.Pending;
        marketplaceOrder.PaymentReferenceId = null;
        // PRODUCTION HARDENING: Do NOT clear PaymentReference during retry.
        // This field is used by ResolveMarketplaceOrderAsync for webhook routing.
        // Clearing it would break late-arriving webhooks from the previous payment session.
        // The field will be overwritten when the new payment is collected.
        // marketplaceOrder.PaymentReference = null; // REMOVED - preserve for webhook routing
        marketplaceOrder.PaidAt = null;

        foreach (var subOrder in affectedSubOrders)
        {
            EnsureCanRetry(subOrder.PaymentStatus, nameof(CommerceHubSubOrder), subOrder.Id);
            subOrder.PaymentStatus = PaymentStatus.Pending;
            subOrder.PaymentReferenceId = null;
            subOrder.PaidAt = null;
        }
    }

    public static void MarkCodSubOrderCollected(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        DateTime nowUtc)
    {
        if (subOrder.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery)
        {
            throw new InvalidOperationException(
                $"CommerceHub COD payment lifecycle cannot collect non-COD sub-order {subOrder.Id}.");
        }

        using var boundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        EnsureCanCollect(subOrder.PaymentStatus, nameof(CommerceHubSubOrder), subOrder.Id);
        subOrder.PaymentStatus = PaymentStatus.Collected;
        subOrder.PaidAt ??= nowUtc;

        var allCodSubOrdersCollected = marketplaceOrder.SubOrders
            .Where(item => item.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery)
            .All(item => item.Id == subOrder.Id || CommerceHubOrderStateHelper.HasCollectedPayment(item.PaymentStatus));

        if (allCodSubOrdersCollected)
        {
            EnsureCanCollect(marketplaceOrder.PaymentStatus, nameof(CommerceHubMarketplaceOrder), marketplaceOrder.Id);
            marketplaceOrder.PaymentStatus = PaymentStatus.Collected;
            marketplaceOrder.PaidAt ??= nowUtc;
        }
    }

    public static void ApplyRefundPaymentState(
        CommerceHubMarketplaceOrder marketplaceOrder,
        CommerceHubSubOrder subOrder,
        bool isFullRefund)
    {
        using var boundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();

        EnsureCanRefund(subOrder.PaymentStatus, nameof(CommerceHubSubOrder), subOrder.Id);
        subOrder.PaymentStatus = isFullRefund
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        marketplaceOrder.PaymentStatus = AggregateMarketplacePaymentStatus(
            marketplaceOrder.SubOrders,
            marketplaceOrder.PaymentStatus);
    }

    public static PaymentStatus AggregateMarketplacePaymentStatus(
        IEnumerable<CommerceHubSubOrder> subOrders,
        PaymentStatus fallbackStatus)
    {
        var statuses = subOrders.Select(subOrder => subOrder.PaymentStatus).ToList();
        if (statuses.Count == 0)
        {
            return fallbackStatus;
        }

        if (statuses.All(status => status == PaymentStatus.Refunded))
        {
            return PaymentStatus.Refunded;
        }

        if (statuses.Any(status => status == PaymentStatus.PartiallyRefunded) ||
            statuses.Any(status => status == PaymentStatus.Refunded))
        {
            return PaymentStatus.PartiallyRefunded;
        }

        if (statuses.All(status => status == PaymentStatus.Settled))
        {
            return PaymentStatus.Settled;
        }

        if (statuses.All(CommerceHubOrderStateHelper.HasCollectedPayment))
        {
            return PaymentStatus.Collected;
        }

        if (statuses.Any(status => status == PaymentStatus.Failed))
        {
            return PaymentStatus.Failed;
        }

        if (statuses.Any(status => status == PaymentStatus.Rejected))
        {
            return PaymentStatus.Rejected;
        }

        return statuses.Any(status => status == PaymentStatus.Pending)
            ? PaymentStatus.Pending
            : fallbackStatus;
    }

    private static void EnsureCanCollect(PaymentStatus currentStatus, string entityName, Guid entityId)
    {
        if (currentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification or PaymentStatus.CashOnDelivery or PaymentStatus.Collected)
        {
            return;
        }

        throw new InvalidOperationException(
            $"CommerceHub payment lifecycle cannot collect {entityName} {entityId} from {currentStatus}.");
    }

    private static void EnsureCanRetry(PaymentStatus currentStatus, string entityName, Guid entityId)
    {
        if (currentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification or PaymentStatus.Failed or PaymentStatus.Rejected)
        {
            return;
        }

        throw new InvalidOperationException(
            $"CommerceHub payment lifecycle cannot reset {entityName} {entityId} to pending from {currentStatus}.");
    }

    private static void EnsureCanRefund(PaymentStatus currentStatus, string entityName, Guid entityId)
    {
        if (currentStatus is PaymentStatus.Collected or PaymentStatus.Settled or PaymentStatus.PartiallyRefunded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"CommerceHub payment lifecycle cannot refund {entityName} {entityId} from {currentStatus}.");
    }
}
