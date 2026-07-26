namespace PointOfSale.Tests.Architecture;

public sealed class DhlRuntimeAuthorityTests
{
    [Fact]
    public void DhlAdapter_MustRemainWireSerializerOnly()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs");

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomsEvidenceAdmissionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddressAdmissionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ICarrierGeographicMappingService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShipmentLifecycleTransitionPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IProduct", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_product", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductVariant", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Inventory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Warehouse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DhlProviderStatuses_MustFailClosedWhenUnknown()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs");

        Assert.Contains("TryMapDhlStatus(statusCode)", source, StringComparison.Ordinal);
        Assert.Contains("return null;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Defaulting to InTransit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogUnknownStatusAndReturnDefault", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendAdmission_MustOwnCarrierResponseAndLifecyclePersistence()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ValidateCarrierCreateResultForPersistence(result)", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_TRACKING_NUMBER_INVALID", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_LABEL_DATA_INVALID", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_LABEL_FORMAT_INVALID", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_METADATA_INVALID", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_STATUS_MAPPING_INVALID", source, StringComparison.Ordinal);
        Assert.Contains("ShipmentLifecycleTransitionPolicy.CanTransition(previousStatus, result.InitialStatus)", source, StringComparison.Ordinal);
        Assert.Contains("CARRIER_STATUS_TRANSITION_INVALID", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DhlRecovery_MustReuseSnapshotsBeforeAdapterInvocation()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ResolveOrRestoreShipmentOriginAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreCarrierGeographyAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreShipmentCustomsAsync", source, StringComparison.Ordinal);
        Assert.Contains("Recovery authority is the immutable shipment evidence", source, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingSnapshotJson", source, StringComparison.Ordinal);
        Assert.Contains("OriginSnapshotJson", source, StringComparison.Ordinal);
        Assert.Contains("Customs = customsResolution.Customs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DhlRuntimeAuthorityReport_MustFreezeBoundary()
    {
        var report = CertificationSource.ReadRepoFile("docs/architecture/DHL-4_RUNTIME_AUTHORITY.md");

        Assert.Contains("DHL owns no Shipment, Order, Checkout, Warehouse, Inventory, Customer, Product, Geography, Customs, SQL Truth, Shipment Lifecycle, or Carrier Mapping authority.", report, StringComparison.Ordinal);
        Assert.Contains("Provider responses remain Evidence only.", report, StringComparison.Ordinal);
        Assert.Contains("Recovery is snapshot-only.", report, StringComparison.Ordinal);
        Assert.Contains("Business logic inside adapter? NO.", report, StringComparison.Ordinal);
    }
}
