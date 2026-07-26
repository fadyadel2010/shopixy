using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Executes carrier registration strictly after the checkout transaction commits.
/// The triggering event is delivered through the EF outbox so external carrier APIs
/// are never called inside the checkout SQL transaction.
/// </summary>
public sealed class ShipmentExternalRegistrationRequestedConsumer
    : IConsumer<ShipmentExternalRegistrationRequestedEvent>
{
    private readonly ICarrierShipmentService _carrierShipmentService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<ShipmentExternalRegistrationRequestedConsumer> _logger;

    public ShipmentExternalRegistrationRequestedConsumer(
        ICarrierShipmentService carrierShipmentService,
        CommerceReliabilityTelemetry telemetry,
        ILogger<ShipmentExternalRegistrationRequestedConsumer> logger)
    {
        _carrierShipmentService = carrierShipmentService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ShipmentExternalRegistrationRequestedEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "Skipping shipment external registration because TenantId is missing | ShipmentId: {ShipmentId}",
                @event.ShipmentId);
            return;
        }

        if (!@event.IsCarrierManaged)
        {
            _logger.LogInformation(
                "Skipping shipment external registration for manual shipment | ShipmentId: {ShipmentId}",
                @event.ShipmentId);
            return;
        }

        var tenantId = @event.TenantId.Value;
        var result = await _carrierShipmentService.ExecuteCarrierForExistingShipmentAsync(
            tenantId,
            @event.ShipmentId,
            @event.ServiceLevel,
            context.CancellationToken);

        if (result.ExternalCallAttempted)
        {
            _telemetry.RecordShipmentExternalCallAfterCommit(
                tenantId,
                @event.CarrierCode,
                result.Success
                    ? result.Reconciled ? "reconciled" : "success"
                    : IsRetryableFailure(result.ErrorCode) ? "retryable_failure" : "failed");
        }

        if (result.Success)
        {
            _logger.LogInformation(
                "Shipment external registration completed | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | ExternalReferenceId: {ExternalReferenceId} | Reconciled: {Reconciled}",
                @event.ShipmentId,
                result.TrackingNumber,
                result.ExternalReferenceId,
                result.Reconciled);
            return;
        }

        _logger.LogError(
            "Shipment external registration failed | ShipmentId: {ShipmentId} | ErrorCode: {ErrorCode} | ErrorMessage: {ErrorMessage}",
            @event.ShipmentId,
            result.ErrorCode,
            result.ErrorMessage);

        if (IsRetryableFailure(result.ErrorCode))
        {
            throw new InvalidOperationException(
                $"Retryable carrier registration failure for shipment {@event.ShipmentId}: {result.ErrorCode} - {result.ErrorMessage}");
        }
    }

    private static bool IsRetryableFailure(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        return string.Equals(errorCode, "HTTP_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "API_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "CONNECTION_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "CARRIER_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "INTERNAL_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "UNEXPECTED_ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "EXTERNAL_REGISTRATION_PERSIST_FAILED", StringComparison.OrdinalIgnoreCase) ||
               errorCode.EndsWith("_API_ERROR", StringComparison.OrdinalIgnoreCase) ||
               errorCode.EndsWith("_CREATE_ERROR", StringComparison.OrdinalIgnoreCase);
    }
}
