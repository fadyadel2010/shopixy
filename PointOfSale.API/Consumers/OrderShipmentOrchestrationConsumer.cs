using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Consumers;

/// <summary>
/// Owns shipment side effects at the order authority boundary.
/// </summary>
public sealed class OrderShipmentOrchestrationConsumer :
    IConsumer<OrderConfirmedEvent>,
    IConsumer<OrderPaidEvent>,
    IConsumer<OrderCollectionAccountingPostedEvent>,
    IConsumer<OrderCancelledEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShipmentService _shipmentService;
    private readonly ICarrierShipmentService _carrierShipmentService;
    private readonly ILogger<OrderShipmentOrchestrationConsumer> _logger;

    public OrderShipmentOrchestrationConsumer(
        ApplicationDbContext dbContext,
        IShipmentService shipmentService,
        ICarrierShipmentService carrierShipmentService,
        ILogger<OrderShipmentOrchestrationConsumer> logger)
    {
        _dbContext = dbContext;
        _shipmentService = shipmentService;
        _carrierShipmentService = carrierShipmentService;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderConfirmedEvent> context)
    {
        var evt = context.Message;
        return EnsureShipmentExistsAsync(
            evt.TenantId.GetValueOrDefault(),
            evt.OrderId,
            evt.CorrelationId,
            "OrderConfirmedEvent",
            context.CancellationToken);
    }

    public Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var evt = context.Message;
        return EnsureShipmentExistsAsync(
            evt.TenantId.GetValueOrDefault(),
            evt.OrderId,
            evt.CorrelationId,
            "OrderPaidEvent",
            context.CancellationToken);
    }

    public Task Consume(ConsumeContext<OrderCollectionAccountingPostedEvent> context)
    {
        var evt = context.Message;
        return EnsureShipmentExistsAsync(
            evt.TenantId.GetValueOrDefault(),
            evt.OrderId,
            evt.CorrelationId,
            "OrderCollectionAccountingPostedEvent",
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderCancelledEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue || evt.TenantId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "Skipping shipment cancellation orchestration because TenantId is missing | OrderId: {OrderId}",
                evt.OrderId);
            return;
        }

        var shipment = await _shipmentService.GetShipmentByOrderIdAsync(
            evt.TenantId.Value,
            evt.OrderId,
            evt.CorrelationId,
            context.CancellationToken);

        if (shipment == null)
        {
            _logger.LogInformation(
                "No shipment found to cancel for cancelled order | TenantId: {TenantId} | OrderId: {OrderId}",
                evt.TenantId.Value,
                evt.OrderId);
            return;
        }

        var cancelResult = await _carrierShipmentService.CancelShipmentAsync(
            evt.TenantId.Value,
            shipment.ShipmentId,
            evt.CancellationReason ?? "Order cancelled",
            context.CancellationToken);

        if (cancelResult.Success)
        {
            _logger.LogInformation(
                "Shipment compensated after order cancellation | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
                evt.TenantId.Value,
                evt.OrderId,
                shipment.ShipmentId);
            return;
        }

        if (string.Equals(cancelResult.ErrorCode, "CANNOT_CANCEL", StringComparison.OrdinalIgnoreCase))
        {
            await MarkShipmentCancellationReconciliationRequiredAsync(
                evt.TenantId.Value,
                shipment.ShipmentId,
                evt.OrderId,
                evt.CancellationReason,
                context.CancellationToken);

            _logger.LogCritical(
                "Shipment cancellation compensation requires operator reconciliation because shipment is already operationally advanced | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
                evt.TenantId.Value,
                evt.OrderId,
                shipment.ShipmentId);
            return;
        }

        throw new InvalidOperationException(
            $"Failed to compensate shipment {shipment.ShipmentId} for cancelled order {evt.OrderId}: {cancelResult.ErrorCode} - {cancelResult.ErrorMessage}");
    }

    private async Task EnsureShipmentExistsAsync(
        Guid tenantId,
        Guid orderId,
        string? correlationId,
        string source,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "Skipping shipment orchestration because TenantId is missing | OrderId: {OrderId} | Source: {Source}",
                orderId,
                source);
            return;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id == orderId && !o.IsDeleted)
            .Select(o => new ShipmentOrderAuthoritySnapshot(
                o.Id,
                o.Status,
                o.ShippingMethodId,
                o.Total,
                o.PaymentMethodCode,
                o.PaymentStatus,
                o.FinancialPostingStatus))
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            _logger.LogWarning(
                "Skipping shipment orchestration because order was not found | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);
            return;
        }

        if (!order.ShippingMethodId.HasValue || order.ShippingMethodId.Value == Guid.Empty)
        {
            _logger.LogInformation(
                "Skipping shipment orchestration because order has no shipping method | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);
            return;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation(
                "Skipping shipment orchestration for cancelled order | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);
            return;
        }

        if (IsEarlyPaymentEvent(source) && RequiresAccountingAuthorityBeforeShipment(order))
        {
            _logger.LogInformation(
                "Deferring shipment orchestration until collection accounting is posted | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source} | PaymentMethodCode: {PaymentMethodCode} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus}",
                tenantId,
                orderId,
                source,
                order.PaymentMethodCode,
                order.PaymentStatus,
                order.FinancialPostingStatus);
            return;
        }

        try
        {
            await _shipmentService.CreateShipmentAsync(
                tenantId,
                orderId,
                order.ShippingMethodId.Value,
                correlationId ?? $"{source}:{orderId:N}",
                cancellationToken);
        }
        catch (ShipmentAlreadyExistsException)
        {
            _logger.LogInformation(
                "Shipment already exists; checking if external registration needs to be completed | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);

            // Check if the existing shipment is in PendingExternalRegistration state
            // If so, attempt to complete the external registration now that payment may be complete
            var existingShipment = await _dbContext.Shipments
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.OrderId == orderId && !s.IsDeleted)
                .Select(s => new
                {
                    s.Id,
                    s.Status,
                    s.IsCarrierManaged,
                    s.ServiceLevel
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (existingShipment != null &&
                existingShipment.Status == ShipmentStatus.PendingExternalRegistration &&
                existingShipment.IsCarrierManaged)
            {
                _logger.LogInformation(
                    "Attempting to complete external registration for existing PendingExternalRegistration shipment | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Source: {Source}",
                    tenantId,
                    orderId,
                    existingShipment.Id,
                    source);

                try
                {
                    var result = await _carrierShipmentService.ExecuteCarrierForExistingShipmentAsync(
                        tenantId,
                        existingShipment.Id,
                        existingShipment.ServiceLevel,
                        cancellationToken);

                    if (result.Success || result.Reconciled)
                    {
                        _logger.LogInformation(
                            "External registration completed for existing shipment{Reconciled} | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Source: {Source}",
                            result.Reconciled ? " (reconciled)" : "",
                            tenantId,
                            orderId,
                            existingShipment.Id,
                            source);
                    }
                    else if (result.ExternalCallSkipped)
                    {
                        _logger.LogInformation(
                            "External registration still skipped for existing shipment (payment may still be incomplete) | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Source: {Source}",
                            tenantId,
                            orderId,
                            existingShipment.Id,
                            source);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "External registration failed for existing shipment | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ErrorCode: {ErrorCode} | ErrorMessage: {ErrorMessage} | Source: {Source}",
                            tenantId,
                            orderId,
                            existingShipment.Id,
                            result.ErrorCode,
                            result.ErrorMessage,
                            source);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to complete external registration for existing shipment | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Source: {Source}",
                        tenantId,
                        orderId,
                        existingShipment.Id,
                        source);
                    // Don't throw - this is a best-effort retry
                }
            }
        }
        catch (OrderNotReadyForShipmentException ex)
        {
            _logger.LogInformation(
                ex,
                "Shipment orchestration skipped because order is not ready | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex,
                "Shipment orchestration skipped because shipment authority conditions are not satisfied | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                tenantId,
                orderId,
                source);
        }
    }

    private static bool IsEarlyPaymentEvent(string source)
        => string.Equals(source, "OrderConfirmedEvent", StringComparison.Ordinal) ||
           string.Equals(source, "OrderPaidEvent", StringComparison.Ordinal);

    private static bool RequiresAccountingAuthorityBeforeShipment(ShipmentOrderAuthoritySnapshot order)
    {
        if (order.Total <= 0m)
        {
            return false;
        }

        if (string.Equals(order.PaymentMethodCode, "COD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(order.PaymentMethodCode, "CASHONDELIVERY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(order.PaymentMethodCode, "CASH_ON_DELIVERY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(order.PaymentMethodCode, "CASH-ON-DELIVERY", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return order.FinancialPostingStatus != FinancialPostingStatus.Posted &&
               order.FinancialPostingStatus != FinancialPostingStatus.NotRequired;
    }

    private async Task MarkShipmentCancellationReconciliationRequiredAsync(
        Guid tenantId,
        Guid shipmentId,
        Guid orderId,
        string? cancellationReason,
        CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Shipments
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == shipmentId &&
                        !item.IsDeleted,
                cancellationToken);

        if (shipment == null)
        {
            return;
        }

        var reason = cancellationReason ?? "Order cancelled";
        var reconciliationMessage =
            $"SHIPMENT_CANCELLATION_RECONCILIATION_REQUIRED: Carrier refused cancellation because shipment is already operationally advanced. Reason={reason}.";

        shipment.ExternalRegistrationState = "ReconciliationRequired";
        shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
        shipment.ExternalRegistrationError = reconciliationMessage.Length <= 500
            ? reconciliationMessage
            : reconciliationMessage[..500];
        shipment.Notes = string.IsNullOrWhiteSpace(shipment.Notes)
            ? reconciliationMessage
            : $"{shipment.Notes}{Environment.NewLine}{reconciliationMessage}";
        shipment.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record ShipmentOrderAuthoritySnapshot(
        Guid Id,
        OrderStatus Status,
        Guid? ShippingMethodId,
        decimal Total,
        string? PaymentMethodCode,
        PaymentStatus PaymentStatus,
        FinancialPostingStatus FinancialPostingStatus);
}
