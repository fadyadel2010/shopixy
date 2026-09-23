namespace PointOfSale.Tests.Architecture;

public sealed class GeographicPersistenceAuthorityTests
{
    [Fact]
    public void Orders_MustPersistFirstClassGeographicSnapshotsWithoutProviderIds()
    {
        var order = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Order.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("ShippingAddressAdmissionEvidenceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingGeographicCatalogReleaseId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingCountryPlaceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingAdministrativeAreaPlaceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingLocalityPlaceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingDistrictPlaceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingZonePlaceId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingGeographicSnapshotVersion", order, StringComparison.Ordinal);
        Assert.Contains("BillingAddressAdmissionEvidenceId", order, StringComparison.Ordinal);
        Assert.Contains("BillingGeographicCatalogReleaseId", order, StringComparison.Ordinal);
        Assert.Contains("ShippingAddressAdmissionEvidenceId = admittedGeography.Shipping?.AddressAdmissionEvidenceId", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", order, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderDistrictId", order, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderZoneId", order, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipments_MustCopyOrderSnapshotAndAvoidLiveCatalogResolution()
    {
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Shipment.cs");
        var shipmentService = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Shipping/ShipmentService.cs");
        var carrierShipmentService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("DestinationAddressAdmissionEvidenceId", shipment, StringComparison.Ordinal);
        Assert.Contains("DestinationGeographicCatalogReleaseId", shipment, StringComparison.Ordinal);
        Assert.Contains("DestinationCountryPlaceId", shipment, StringComparison.Ordinal);
        Assert.Contains("DestinationGeographicSnapshotVersion", shipment, StringComparison.Ordinal);
        Assert.Contains("DestinationAddressAdmissionEvidenceId = order.ShippingAddressAdmissionEvidenceId", shipmentService, StringComparison.Ordinal);
        Assert.Contains("DestinationAddressAdmissionEvidenceId = order.ShippingAddressAdmissionEvidenceId", carrierShipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddressAdmissionService", shipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddressAdmissionService", carrierShipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicCatalogService", shipmentService, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicCatalogService", carrierShipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedAddresses_MustBeVersionedAdmittedSnapshots()
    {
        var customer = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Customer.cs");
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("public class CustomerAddress", customer, StringComparison.Ordinal);
        Assert.Contains("public class CustomerAddressVersion", customer, StringComparison.Ordinal);
        Assert.Contains("AddressAdmissionEvidenceId", customer, StringComparison.Ordinal);
        Assert.Contains("GeographicCatalogReleaseId", customer, StringComparison.Ordinal);
        Assert.Contains("SnapshotVersion", customer, StringComparison.Ordinal);
        Assert.Contains("Customer address versions are immutable geographic snapshots", db, StringComparison.Ordinal);
        Assert.Contains("AddressAdmissionLinked", db, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", customer, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceValidation_MustFailClosedAndAuditSnapshots()
    {
        var db = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("GuardGeographicBusinessSnapshotPersistence", db, StringComparison.Ordinal);
        Assert.Contains("geographic snapshot is broken and cannot be persisted", db, StringComparison.Ordinal);
        Assert.Contains("geographic snapshot is immutable and cannot be updated after persistence", db, StringComparison.Ordinal);
        Assert.Contains("OrderGeographicSnapshotCreated", db, StringComparison.Ordinal);
        Assert.Contains("ShipmentGeographicSnapshotCreated", db, StringComparison.Ordinal);
        Assert.Contains("ShippingAddressAdmissionEvidenceId = table.Column<Guid>", migration, StringComparison.Ordinal);
        Assert.Contains("DestinationAddressAdmissionEvidenceId = table.Column<Guid>", migration, StringComparison.Ordinal);
        Assert.Contains("SnapshotVersion = table.Column<int>", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureDocument_MustFreezeSnapshotOnlyRuntimeAuthority()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/GEO-8_ORDER_AND_SHIPMENT_GEOGRAPHIC_PERSISTENCE.md");

        Assert.Contains("Orders and Shipments are immutable historical documents", document, StringComparison.Ordinal);
        Assert.Contains("They MUST NEVER depend on the live Geographic Catalog", document, StringComparison.Ordinal);
        Assert.Contains("After persistence, runtime consumes snapshots only", document, StringComparison.Ordinal);
        Assert.Contains("Provider IDs persisted? **NO**", document, StringComparison.Ordinal);
    }
}
