using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.CommerceHub;

public static class CommerceHubReturnMapper
{
    public static CommerceHubReturnDetailsDto MapDetails(
        CommerceHubReturnRequest returnRequest,
        CommerceHubRefundRequest refundRequest)
    {
        return new CommerceHubReturnDetailsDto
        {
            ReturnId = returnRequest.Id,
            RefundId = returnRequest.RefundId,
            OrderItemId = returnRequest.OrderItemId,
            CustomerId = returnRequest.CustomerId,
            VendorId = returnRequest.VendorId,
            Status = returnRequest.Status,
            RefundExecutionTrigger = refundRequest.RefundExecutionTrigger,
            PickupScheduledAt = returnRequest.PickupScheduledAt,
            PickedUpAt = returnRequest.PickedUpAt,
            InTransitAt = returnRequest.InTransitAt,
            DeliveredAt = returnRequest.DeliveredAt,
            ConfirmedAt = returnRequest.ConfirmedAt,
            TrackingNumber = returnRequest.TrackingNumber,
            CourierName = returnRequest.CourierName,
            Notes = returnRequest.Notes,
            RequestedQuantity = returnRequest.RequestedQuantity,
            ReceivedResellableQuantity = returnRequest.ReceivedResellableQuantity,
            ReceivedDamagedQuantity = returnRequest.ReceivedDamagedQuantity,
            CreatedAt = returnRequest.CreatedAt,
            UpdatedAt = returnRequest.UpdatedAt,
            CanSchedulePickup = CommerceHubReturnPolicy.CanSchedulePickup(returnRequest.Status),
            CanMarkPickedUp = CommerceHubReturnPolicy.CanMarkPickedUp(returnRequest.Status),
            CanMarkInTransit = CommerceHubReturnPolicy.CanMarkInTransit(returnRequest.Status),
            CanMarkDelivered = CommerceHubReturnPolicy.CanMarkDelivered(returnRequest.Status),
            CanMarkFailed = CommerceHubReturnPolicy.CanMarkFailed(returnRequest.Status) &&
                            !CommerceHubRefundRequestWorkflowPolicy.IsExecuted(refundRequest.Status),
            CanConfirmReceipt = CommerceHubReturnPolicy.CanConfirmReceipt(returnRequest.Status),
            IsExecutionTriggerReached = CommerceHubReturnPolicy.IsExecutionTriggerReached(
                refundRequest.RefundExecutionTrigger,
                returnRequest.Status)
        };
    }

    public static CustomerReturnSummaryDto MapCustomerSummary(CommerceHubReturnRequest returnRequest)
    {
        var refundRequest = returnRequest.Refund;
        return new CustomerReturnSummaryDto
        {
            ReturnId = returnRequest.Id,
            RefundId = refundRequest.Id,
            OrderId = refundRequest.OrderId,
            OrderNumber = refundRequest.Order.OrderNumber,
            OrderItemId = returnRequest.OrderItemId,
            VendorId = returnRequest.VendorId,
            ProductName = returnRequest.OrderItem.ProductName,
            VariantName = returnRequest.OrderItem.VariantName,
            Quantity = refundRequest.Quantity,
            RefundAmount = refundRequest.TotalAmount,
            Currency = CommerceHubRefundReturnCurrencyAuthority.RequireOrderCurrency(
                refundRequest.Order,
                $"return-response:{returnRequest.Id:D}"),
            RefundStatus = refundRequest.Status,
            ReturnRequest = MapDetails(returnRequest, refundRequest)
        };
    }

    public static CustomerReturnDetailsDto MapCustomerDetails(CommerceHubReturnRequest returnRequest)
    {
        var refundRequest = returnRequest.Refund;
        return new CustomerReturnDetailsDto
        {
            ReturnId = returnRequest.Id,
            RefundId = refundRequest.Id,
            OrderId = refundRequest.OrderId,
            OrderNumber = refundRequest.Order.OrderNumber,
            OrderItemId = returnRequest.OrderItemId,
            VendorId = returnRequest.VendorId,
            ProductName = returnRequest.OrderItem.ProductName,
            VariantName = returnRequest.OrderItem.VariantName,
            Quantity = refundRequest.Quantity,
            RefundAmount = refundRequest.TotalAmount,
            Currency = CommerceHubRefundReturnCurrencyAuthority.RequireOrderCurrency(
                refundRequest.Order,
                $"return-response:{returnRequest.Id:D}"),
            RefundStatus = refundRequest.Status,
            RefundReason = CustomerRefundService.BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
            ReturnRequest = MapDetails(returnRequest, refundRequest)
        };
    }
}
