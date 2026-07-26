# COMPILATION FIX - IMMEDIATE ACTION REQUIRED

## Problem
The OrderFulfillmentService has old method signatures mixed with new ones, causing compilation errors.

## Quick Fix (5 minutes)

Add these stub methods to `OrderFulfillmentService.cs` after line 120 (after MarkOutForDeliveryAsync):

```csharp
public async Task<OrderFulfillmentResult> MarkDeliveredAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string? deliveryReference = null,
    string? proofOfDeliveryUrl = null,
    string? recipientName = null,
    string? deliveredBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.Delivered,
        deliveredBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new FulfillmentDeliveredEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = deliveredBy,
            TransitionedAt = DateTime.UtcNow,
            DeliveryReference = deliveryReference,
            ProofOfDeliveryUrl = proofOfDeliveryUrl,
            RecipientName = recipientName,
            PaymentMethod = order.PaymentMethodCode ?? "unknown",
            PaymentStatus = order.PaymentStatus,
            OrderTotal = order.Total,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}

public async Task<OrderFulfillmentResult> MarkCompletedAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string? completedBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.Completed,
        completedBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new OrderCompletedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = completedBy,
            TransitionedAt = DateTime.UtcNow,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}

public async Task<OrderFulfillmentResult> MarkReturnedAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string reason,
    bool shouldRefund = false,
    decimal? refundAmount = null,
    string? returnAuthorizationNumber = null,
    string? returnedBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    if (order == null)
        return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.Returned,
        returnedBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new OrderReturnedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = returnedBy,
            TransitionedAt = DateTime.UtcNow,
            Reason = reason,
            ReturnAuthorizationNumber = returnAuthorizationNumber,
            ShouldRefund = shouldRefund,
            RefundAmount = refundAmount ?? order.Total,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}

public async Task<OrderFulfillmentResult> MarkDeliveryFailedAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string reason,
    int attemptNumber = 1,
    bool canRetry = true,
    string? updatedBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    if (order == null)
        return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.FailedDelivery,
        updatedBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new OrderDeliveryFailedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = updatedBy,
            TransitionedAt = DateTime.UtcNow,
            Reason = reason,
            AttemptNumber = attemptNumber,
            CanRetry = canRetry,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}

public async Task<OrderFulfillmentResult> RetryDeliveryAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string? updatedBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.OutForDelivery,
        updatedBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new OrderOutForDeliveryEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = updatedBy,
            TransitionedAt = DateTime.UtcNow,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}

public async Task<OrderFulfillmentResult> CancelAsync(
    Guid orderId,
    FulfillmentRequestSource source,
    string reason,
    bool releaseInventory = true,
    string? cancelledBy = null,
    string? idempotencyKey = null,
    CancellationToken ct = default)
{
    var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
    if (order == null)
        return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

    return await TransitionAsync(
        orderId,
        source,
        FulfillmentStatus.Cancelled,
        cancelledBy,
        idempotencyKey,
        (order, oldStatus, newStatus) => new OrderFulfillmentCancelledEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TransitionedBy = cancelledBy,
            TransitionedAt = DateTime.UtcNow,
            Reason = reason,
            CancelledBy = cancelledBy,
            ReleaseInventory = releaseInventory,
            Source = source,
            IdempotencyKey = idempotencyKey
        },
        ct);
}
```

## Then Delete Old Methods

Delete all the old method implementations from line ~196 to ~550 (everything between the new methods and GetAvailableActionsAsync).

## Verify

Run `dotnet build` - it should compile successfully.

## Status

This is a TEMPORARY fix to get the code compiling. The full security hardening implementation is documented in:
- FULFILLMENT_OWNERSHIP_SECURITY_HARDENING.md
- SECURITY_HARDENING_IMPLEMENTATION_SUMMARY.md
