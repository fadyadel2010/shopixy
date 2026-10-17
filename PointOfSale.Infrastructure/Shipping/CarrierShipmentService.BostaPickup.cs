using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    private async Task<BostaPickupOrigin?> ResolveBostaPickupOriginAsync(Shipment shipment, Order order,
        CarrierCredentialsResolution resolution, IShippingCarrierAdapter adapter, CancellationToken ct)
    {
        if (resolution.Configuration == null || resolution.Credentials == null || adapter is not IBostaPickupLocationDiscovery discovery ||
            !order.WarehouseId.HasValue || !await _dbContext.Warehouses.AsNoTracking().AnyAsync(w => w.Id == order.WarehouseId &&
                w.TenantId == shipment.TenantId && w.IsActive && !w.IsDeleted, ct)) return null;
        var result = await discovery.GetPickupLocationsAsync(resolution.Credentials, ct);
        var pickup = BostaPickupSelections.Resolve(resolution.Credentials, result).Location;
        if (pickup == null) return null;
        return new(1, shipment.TenantId, order.WarehouseId.Value, resolution.Configuration.Id, resolution.Credentials.UseSandbox,
            pickup.Id, pickup.CountryCode!, pickup.CountryId!, pickup.CityId!, pickup.DistrictId!, DateTime.UtcNow);
    }

    private static bool MatchesBostaPickup(BostaPickupOrigin? pickup, Shipment shipment, Order order, CarrierCredentialsResolution resolution) =>
        pickup != null && pickup.Version == 1 && pickup.TenantId == shipment.TenantId && pickup.InventoryWarehouseId == order.WarehouseId &&
        pickup.ConfigurationId == resolution.Configuration?.Id && pickup.UseSandbox == resolution.Credentials?.UseSandbox &&
        (!resolution.Credentials.ExtraConfig.TryGetValue("businessLocationId", out var selected) || selected == pickup.BusinessLocationId) && pickup.CountryCode == "EG" &&
        !string.IsNullOrWhiteSpace(pickup.CountryId) && !string.IsNullOrWhiteSpace(pickup.CityId) && !string.IsNullOrWhiteSpace(pickup.DistrictId);
}
