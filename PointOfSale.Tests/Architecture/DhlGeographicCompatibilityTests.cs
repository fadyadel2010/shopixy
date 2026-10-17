namespace PointOfSale.Tests.Architecture;

public sealed class DhlGeographicCompatibilityTests
{
    [Fact]
    public void DhlAdapter_DoesNotResolveOrLookupGeography()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs");

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGeographic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierGeographicMappingService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicPlace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressAdmissionEvidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FromSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("address-validate", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DhlCreateRequest_UsesShipmentOriginDestinationAndMappingEvidenceBeforeAdapterCall()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ResolveOrRestoreShipmentOriginAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreCarrierGeographyAsync", source, StringComparison.Ordinal);
        Assert.Contains("OriginName = origin.ContactName", source, StringComparison.Ordinal);
        Assert.Contains("OriginAddress = origin.Street", source, StringComparison.Ordinal);
        Assert.Contains("OriginCountry = origin.CountryIso2", source, StringComparison.Ordinal);
        Assert.Contains("Origin = origin", source, StringComparison.Ordinal);
        Assert.Contains("RecipientAddress = shipment.DestinationStreet", source, StringComparison.Ordinal);
        Assert.Contains("RecipientCity = shipment.DestinationLocalityDisplayName", source, StringComparison.Ordinal);
        Assert.Contains("RecipientStateOrProvinceCode = shipment.DestinationAdministrativeAreaDisplayName", source, StringComparison.Ordinal);
        Assert.Contains("RecipientCountry = geographicResolution.CountryIso2", source, StringComparison.Ordinal);
        Assert.Contains("ResolvedProviderGeography = geographicResolution.ProviderItems", source, StringComparison.Ordinal);
        Assert.Contains("ApplyLegacyProviderGeographyFields(createRequest, geographicResolution.ProviderItems)", source, StringComparison.Ordinal);
        var dhlBuilders = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.ParcelEvidence.cs");
        Assert.Equal(2, dhlBuilders.Split("OriginZipCode = origin.PostalCode", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DhlAdapter_DoesNotSerializeProviderGeographicIdentifiers()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs");

        Assert.DoesNotContain("RecipientCityId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RecipientDistrictId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RecipientZoneId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvedProviderGeography", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderGeographicId", source, StringComparison.Ordinal);
    }
}
