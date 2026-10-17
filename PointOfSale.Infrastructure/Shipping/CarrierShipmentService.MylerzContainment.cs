using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    private static bool HasUnresolvedMylerzRegistrationEvidence(Shipment shipment) =>
        shipment.ExternalRegistrationAttemptCount > 0 ||
        shipment.ExternalRegistrationLastAttemptAtUtc.HasValue ||
        shipment.ExternalRegistrationCompletedAtUtc.HasValue ||
        !string.IsNullOrWhiteSpace(shipment.CarrierRequestSnapshotJson) ||
        !string.IsNullOrWhiteSpace(shipment.CarrierRequestFingerprint) ||
        !string.IsNullOrWhiteSpace(shipment.CarrierCreateResponse) ||
        IsShipmentExternallyRegistered(shipment) ||
        shipment.ExternalRegistrationState == ExternalRegistrationStateReconciliationRequired;

    private async Task<CarrierShipmentResult> ContainMylerzRegistrationAsync(
        Shipment shipment, CancellationToken cancellationToken)
    {
        // Historical identifiers are not provider-confirmed completion. Preserve
        // the original request, attempt timestamps/count and identity evidence;
        // neither unavailable service levels nor credential edits allow re-creation.
        const string errorCode = "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED";
        const string errorMessage = "Prior Mylerz registration evidence cannot be verified automatically; no authoritative Egypt merchant lookup is established. Replacement creation is blocked.";
        shipment.ExternalRegistrationState = ExternalRegistrationStateReconciliationRequired;
        shipment.ExternalRegistrationError = $"{errorCode}:{errorMessage}";
        shipment.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CarrierShipmentResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ExternalCallSkipped = true,
            Reconciled = true
        };
    }
}
