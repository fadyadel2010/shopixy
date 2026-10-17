using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class CarrierGeographicResolutionIntegrationTests
{
    [Fact]
    public void ShipmentRegistration_ResolvesBeforeEveryAdapterCreateCall_AndRecoveryUsesSnapshot()
    {
        var source = Read("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("ResolveOrRestoreCarrierGeographyAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolvedProviderGeography = geographicResolution.ProviderItems", source, StringComparison.Ordinal);
        Assert.Contains("Recovery authority is the immutable shipment evidence", source, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingSnapshotJson", source, StringComparison.Ordinal);
        Assert.Contains("CarrierResolutionFailure", source, StringComparison.Ordinal);

        var resolveIndex = source.IndexOf("ResolveOrRestoreCarrierGeographyAsync(", StringComparison.Ordinal);
        var adapterCallIndex = source.IndexOf("adapter.CreateShipmentAsync(", StringComparison.Ordinal);
        Assert.True(resolveIndex >= 0 && adapterCallIndex > resolveIndex);
    }

    [Fact]
    public void Resolver_FailsClosedAcrossPublicationProviderScopeCountryAndAmbiguity()
    {
        var source = Read("PointOfSale.Infrastructure/Geographic/GeographicRepository.cs");

        Assert.Contains("GeographicReleaseStatus.Published", source, StringComparison.Ordinal);
        Assert.Contains("candidate.PublishedAtUtc != null", source, StringComparison.Ordinal);
        Assert.Contains("publication.ActivatedAtUtc != null", source, StringComparison.Ordinal);
        Assert.Contains("mapping.CarrierProviderId == provider.Id", source, StringComparison.Ordinal);
        Assert.Contains("mapping.CountryPlaceId == request.Lineage.CountryPlaceId", source, StringComparison.Ordinal);
        Assert.Contains("AmbiguousProviderMapping", source, StringComparison.Ordinal);
        Assert.Contains("StaleMappingRelease", source, StringComparison.Ordinal);
        Assert.Contains("MissingRequiredProviderMapping", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_EnforcesCountryRules_AndAramexDestinationUsesResolvedSnapshotEvidence()
    {
        var contracts = Read("PointOfSale.Core/Interfaces/Geographic/GeographicContracts.cs");
        var resolver = Read("PointOfSale.Infrastructure/Geographic/GeographicRepository.cs");
        var registration = Read("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");

        Assert.Contains("Guid CatalogReleaseId", contracts, StringComparison.Ordinal);
        Assert.Contains("string? PostalCode", contracts, StringComparison.Ordinal);
        Assert.Contains("bool HasStreetLine1", contracts, StringComparison.Ordinal);
        Assert.Contains("candidate.CatalogReleaseId == request.CatalogReleaseId", resolver, StringComparison.Ordinal);
        Assert.Contains("UnsupportedCountry", resolver, StringComparison.Ordinal);
        Assert.Contains("RequiredAdministrativeLevelMissing", resolver, StringComparison.Ordinal);
        Assert.Contains("PostalCodeRequired", resolver, StringComparison.Ordinal);
        Assert.Contains("PostalCodeInvalid", resolver, StringComparison.Ordinal);
        Assert.Contains("StreetLine1Required", resolver, StringComparison.Ordinal);
        Assert.Contains("RecipientCity = shipment.DestinationLocalityDisplayName", registration, StringComparison.Ordinal);
        Assert.Contains("RecipientStateOrProvinceCode = shipment.DestinationAdministrativeAreaDisplayName", registration, StringComparison.Ordinal);
        Assert.Contains("RecipientCountry = geographicResolution.CountryIso2", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("RecipientCountry = shipment.ShippingCountry ?? order.ShippingCountry ?? \"EG\"", registration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BostaAdapter.cs")]
    [InlineData("AramexAdapter.cs")]
    [InlineData("MylerzAdapter.cs")]
    [InlineData("DhlAdapter.cs")]
    [InlineData("FedExAdapter.cs")]
    [InlineData("ManualCarrierDefinition.cs")]
    public void CarrierAdapters_DoNotQueryGeographicAuthority(string adapterFile)
    {
        var source = Read($"PointOfSale.Infrastructure/Shipping/Adapters/{adapterFile}");

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGeographic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierGeographicMappingService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GeographicPlaceAlias", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(FromSql|ExecuteSql|DbSet)\b", RegexOptions.CultureInvariant), source);
    }

    [Fact]
    public void ProviderIdentifiers_ArePersistedOnlyAsShipmentRegistrationEvidence()
    {
        var shipment = Read("PointOfSale.Core/Entities/Shipment.cs");
        var order = Read("PointOfSale.Core/Entities/Order.cs");
        var customerAddress = Read("PointOfSale.Core/Entities/Customer.cs");
        const string retiredMigration = "20260705200000_Geo9CarrierGeographicResolution.cs";
        Assert.False(File.Exists(Path.Combine(FindRoot(), "PointOfSale.Infrastructure/Migrations", retiredMigration)));
        var migration = Read($"docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/{retiredMigration}");
        var baseline = Read("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");
        Assert.Contains("CarrierGeographicMappingSnapshotJson", baseline, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingReleaseId", baseline, StringComparison.Ordinal);

        Assert.Contains("CarrierGeographicMappingSnapshotJson", shipment, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingReleaseId", shipment, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", order, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", customerAddress, StringComparison.Ordinal);
        Assert.Contains("\"Shipments\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE \"Orders\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE \"Customer", migration, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var root = FindRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
