namespace PointOfSale.Infrastructure.Shipping;

// Provider pickup authority is not a physical Shopixy warehouse address.
// InventoryWarehouseId retains allocation only; no street/contact is invented.
public sealed record BostaPickupOrigin(int Version, Guid TenantId, Guid InventoryWarehouseId,
    Guid ConfigurationId, bool UseSandbox, string BusinessLocationId, string CountryCode,
    string CountryId, string CityId, string DistrictId, DateTime ObservedAtUtc);
