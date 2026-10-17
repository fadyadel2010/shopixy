namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Original persisted delivery identity, never supplied by an arbitrary UI tracking string.</summary>
public sealed record CarrierCancellationTarget(string TrackingNumber, string ExternalReferenceId);

public interface IIdentityBoundCarrierCancellation
{
    Task<CancelShipmentResult> CancelShipmentAsync(CarrierCancellationTarget target, CarrierCredentials credentials,
        CancellationToken cancellationToken = default);
}
