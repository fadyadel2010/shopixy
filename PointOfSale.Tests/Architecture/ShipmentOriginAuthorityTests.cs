using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Architecture;

public sealed class ShipmentOriginAuthorityTests
{
    [Fact]
    public void Shipment_PersistsCanonicalOriginSnapshot()
    {
        var properties = typeof(Shipment).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.Contains(nameof(Shipment.OriginWarehouseId), properties);
        Assert.Contains(nameof(Shipment.OriginAddressAdmissionEvidenceId), properties);
        Assert.Contains(nameof(Shipment.OriginGeographicCatalogReleaseId), properties);
        Assert.Contains(nameof(Shipment.OriginCountryPlaceId), properties);
        Assert.Contains(nameof(Shipment.OriginSnapshotJson), properties);
    }

    [Fact]
    public void CarrierRequest_ExposesCanonicalOriginDto()
    {
        Assert.Equal(typeof(ShipmentOriginDto), typeof(CreateShipmentRequest).GetProperty(nameof(CreateShipmentRequest.Origin))!.PropertyType);
    }

    [Fact]
    public void Registration_MustNotBuildOriginFromCarrierConfiguration()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.DoesNotContain("OriginName = carrierConfig.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginAddress = carrierConfig.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginCity = carrierConfig.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginCountry = carrierConfig.", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreShipmentOriginAsync", source, StringComparison.Ordinal);
        Assert.Contains("AddressAdmissionOwnerDomain.Warehouse", source, StringComparison.Ordinal);
        Assert.Contains("ShipmentOriginAdmissionFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_MustReuseStoredOriginSnapshot()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("JsonSerializer.Deserialize<ShipmentOriginDto>(shipment.OriginSnapshotJson)", source, StringComparison.Ordinal);
        Assert.Contains("shipment.ExternalRegistrationCompletedAtUtc.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("GuardSnapshotPropertyMutation(entry, \"Origin\", \"Shipment origin\")", db, StringComparison.Ordinal);
    }
}
