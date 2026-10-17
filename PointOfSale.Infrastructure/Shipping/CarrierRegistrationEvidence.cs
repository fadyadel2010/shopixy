using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Conservative no-repost boundary, not proof of remote success or absence.</summary>
public static class CarrierRegistrationEvidence
{
    public static bool HasOutboundEvidence(Shipment shipment) =>
        shipment.ExternalRegistrationAttemptCount != 0 ||
        shipment.ExternalRegistrationLastAttemptAtUtc.HasValue || shipment.ExternalRegistrationCompletedAtUtc.HasValue ||
        !string.IsNullOrWhiteSpace(shipment.ExternalReferenceId) || !string.IsNullOrWhiteSpace(shipment.TrackingNumber) ||
        !string.IsNullOrWhiteSpace(shipment.AwbNumber) || shipment.CarrierCreateResponse != null ||
        shipment.CarrierRequestSnapshotJson != null || shipment.CarrierRequestFingerprint != null ||
        shipment.ExternalRegistrationState == "ReconciliationRequired";
    // A scheduled request timestamp or locally generated request ID alone is not
    // an outbound attempt and must not strand a legitimate first execution.

    public static bool RequiresReconciliation(Shipment shipment) => shipment.IsCarrierManaged &&
        (shipment.ExternalRegistrationState == "ReconciliationRequired" ||
         (HasOutboundEvidence(shipment) && (!shipment.ExternalRegistrationCompletedAtUtc.HasValue ||
          (string.IsNullOrWhiteSpace(shipment.ExternalReferenceId) && string.IsNullOrWhiteSpace(shipment.TrackingNumber) &&
           string.IsNullOrWhiteSpace(shipment.AwbNumber)))));
}
