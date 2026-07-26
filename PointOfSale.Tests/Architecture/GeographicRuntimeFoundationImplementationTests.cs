using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class GeographicRuntimeFoundationImplementationTests
{
    [Fact]
    public void Geo5EntityModel_MustRegisterApprovedGeographicRuntimeTables()
    {
        using var context = CreateContext();
        var entityTypes = context.Model.GetEntityTypes()
            .Select(entity => entity.ClrType)
            .ToHashSet();

        foreach (var requiredType in new[]
                 {
                     typeof(GeographicPlace),
                     typeof(GeographicPlaceLevel),
                     typeof(GeographicPlaceHierarchyEdge),
                     typeof(GeographicPlaceName),
                     typeof(GeographicPlaceAlias),
                     typeof(GeographicCountryProfile),
                     typeof(GeographicCatalogRelease),
                     typeof(GeographicCatalogPublication),
                     typeof(CarrierProvider),
                     typeof(CarrierGeographicMappingRelease),
                     typeof(CarrierGeographicMapping),
                     typeof(CarrierGeographicSyncRun),
                     typeof(CarrierGeographicSyncCandidate),
                     typeof(CarrierGeographicOverride),
                     typeof(PlaceMergeHistory),
                     typeof(PlaceDeprecation),
                     typeof(AddressAdmissionEvidence),
                     typeof(AddressNormalizationRule),
                     typeof(GeographicAudit)
                 })
        {
            Assert.Contains(requiredType, entityTypes);
        }
    }

    [Fact]
    public void Geo5ServiceContracts_MustRemainFoundationalAndProviderIndependent()
    {
        foreach (var requiredContract in new[]
                 {
                     typeof(IGeographicCatalogService),
                     typeof(IGeographicResolver),
                     typeof(IGeographicPublicationService),
                     typeof(ICarrierGeographicMappingService),
                     typeof(IGeographicSynchronizationService),
                     typeof(IAddressAdmissionService),
                     typeof(IGeographicRepository)
                 })
        {
            Assert.True(requiredContract.IsInterface, $"{requiredContract.Name} must stay a service contract.");
        }

        var contractSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Interfaces/Geographic/GeographicContracts.cs");
        Assert.Contains("ResolvePublishedMappingAsync", contractSource, StringComparison.Ordinal);
        Assert.Contains("GeographicAddressCandidate", contractSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutService", contractSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShipmentService", contractSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BostaAdapter", contractSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo5Migration_MustBeAdditiveAndGeographicOnly()
    {
        var migration = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260705170009_InitialGeographicRuntimeFoundation.cs");

        foreach (var requiredTable in new[]
                 {
                     "GeographicPlaces",
                     "GeographicPlaceLevels",
                     "GeographicPlaceHierarchyEdges",
                     "GeographicPlaceNames",
                     "GeographicPlaceAliases",
                     "GeographicCountryProfiles",
                     "GeographicCatalogReleases",
                     "GeographicCatalogPublications",
                     "CarrierProviders",
                     "CarrierGeographicMappingReleases",
                     "CarrierGeographicMappings",
                     "CarrierGeographicSyncRuns",
                     "CarrierGeographicSyncCandidates",
                     "CarrierGeographicOverrides",
                     "PlaceMergeHistory",
                     "PlaceDeprecations",
                     "AddressAdmissionEvidence",
                     "AddressNormalizationRules",
                     "GeographicAudit"
                 })
        {
            Assert.Contains(requiredTable, migration, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("DropColumn(", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DropTable(", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn(", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DropForeignKey(", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutSessions", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("Shipments", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("Orders", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierConfigurations", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo5RuntimeFoundation_MustPreserveAuthorityRules()
    {
        var implementation = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Geographic/GeographicRepository.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("Status == GeographicReleaseStatus.Published", implementation, StringComparison.Ordinal);
        Assert.Contains("PublishedCatalogReleaseRequired", implementation, StringComparison.Ordinal);
        Assert.Contains("PublishedMappingReleaseRequired", implementation, StringComparison.Ordinal);
        Assert.Contains("StaleMappingRelease", implementation, StringComparison.Ordinal);
        Assert.Contains("PublicationWorkflowNotImplementedInFoundation", implementation, StringComparison.Ordinal);
        Assert.Contains("RollbackWorkflowNotImplementedInFoundation", implementation, StringComparison.Ordinal);
        Assert.Contains("GuardGeographicReleaseAndAuditAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("immutable after publication", dbContext, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
