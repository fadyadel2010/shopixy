namespace PointOfSale.Infrastructure.Shipping;

public sealed record BostaGeographicReference(string Type, string Id, string Name, string? Code,
    string? ParentId, bool PickupAvailable, bool DropOffAvailable);
public sealed record BostaGeographyResult(bool Succeeded, IReadOnlyList<BostaGeographicReference> Places,
    string? ErrorCode, DateTime ObservedAtUtc);
public interface IBostaGeographyDiscovery
{
    Task<BostaGeographyResult> GetGeographyAsync(CarrierCredentials credentials, string? cityId,
        CancellationToken cancellationToken = default);
}
