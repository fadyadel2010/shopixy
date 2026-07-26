namespace PointOfSale.Tests.Architecture;

public sealed class GeographicSqlModelArchitectureTests
{
    [Fact]
    public void Geo2SqlModel_MustExistAndPreserveApprovedArchitectureBoundaries()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

        Assert.Contains("Status: Design freeze pending approval", geo2, StringComparison.Ordinal);
        Assert.Contains("Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`", geo2, StringComparison.Ordinal);
        Assert.Contains("Runtime impact: none", geo2, StringComparison.Ordinal);
        Assert.Contains("EF impact: none", geo2, StringComparison.Ordinal);
        Assert.Contains("Migration impact: none", geo2, StringComparison.Ordinal);

        Assert.Contains("Geographic owns canonical place identity", geo2, StringComparison.Ordinal);
        Assert.Contains("Business domains own checkout candidates", geo2, StringComparison.Ordinal);
        Assert.Contains("Provider identifiers appear only in carrier geographic mapping tables", geo2, StringComparison.Ordinal);
        Assert.Contains("They never", geo2, StringComparison.Ordinal);
        Assert.Contains("appear in checkout", geo2, StringComparison.Ordinal);
        Assert.Contains("orders, customers", geo2, StringComparison.Ordinal);
        Assert.Contains("customers, saved addresses, address books", geo2, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo2SqlModel_MustFreezeRequiredEntityFamilies()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

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
            Assert.Contains(requiredTable, geo2, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Geo2Hierarchy_MustRemainCountryNeutralAndReleaseValidated()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

        Assert.Contains("The hierarchy supports any country through `GeographicPlaceLevels`", geo2, StringComparison.Ordinal);
        Assert.Contains("The base place kinds are broad", geo2, StringComparison.Ordinal);
        Assert.Contains("`Country`, `AdministrativeArea`, `Locality`", geo2, StringComparison.Ordinal);
        Assert.Contains("Egypt", geo2, StringComparison.Ordinal);
        Assert.Contains("can display \"Governorate\"", geo2, StringComparison.Ordinal);
        Assert.Contains("another country can", geo2, StringComparison.Ordinal);
        Assert.Contains("display \"Province\"", geo2, StringComparison.Ordinal);
        Assert.Contains("Cycle prevention", geo2, StringComparison.Ordinal);
        Assert.Contains("parent/type compatibility", geo2, StringComparison.Ordinal);
        Assert.Contains("publication-time", geo2, StringComparison.Ordinal);
        Assert.Contains("Separate table per level", geo2, StringComparison.Ordinal);
        Assert.Contains("Rejected.", geo2, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo2ProviderMappings_MustBeReleaseScopedAndExcludedFromBusinessAddressModels()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

        Assert.Contains("Provider Mapping Strategy", geo2, StringComparison.Ordinal);
        Assert.Contains("Provider identifiers are isolated to the Provider Geographic Mapping aggregate.", geo2, StringComparison.Ordinal);
        Assert.Contains("Runtime resolution must use a published mapping release", geo2, StringComparison.Ordinal);
        Assert.Contains("Forbidden columns:", geo2, StringComparison.Ordinal);
        Assert.Contains("no provider city/district/zone/region ID columns in orders", geo2, StringComparison.Ordinal);
        Assert.Contains("no provider identifiers in checkout sessions", geo2, StringComparison.Ordinal);
        Assert.Contains("no provider identifiers in customer/saved address/address-book rows", geo2, StringComparison.Ordinal);
        Assert.Contains("no provider identifiers in generic warehouse/store/branch address records", geo2, StringComparison.Ordinal);
        Assert.Contains("no provider identifiers in `CarrierMetadata` as authority", geo2, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo2Snapshots_MustBeImmutableAndIndependentFromLiveCatalogRows()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

        Assert.Contains("Business snapshots must never depend on live catalog or mapping rows", geo2, StringComparison.Ordinal);
        Assert.Contains("Orders must not persist provider IDs.", geo2, StringComparison.Ordinal);
        Assert.Contains("Shipment origin, destination, return destination, and pickup snapshots", geo2, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingReleaseId", geo2, StringComparison.Ordinal);
        Assert.Contains("Tracking, webhook, sync, provider response, catalog rename, mapping rollback", geo2, StringComparison.Ordinal);
        Assert.Contains("must not update these snapshots", geo2, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo2RegressionArtifacts_MustRecordDesignOnlyOutcome()
    {
        var geo2 = CertificationSource.ReadRepoFile("docs/architecture/GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md");

        Assert.Contains("Runtime files modified: **No**", geo2, StringComparison.Ordinal);
        Assert.Contains("SQL entities added: **No physical SQL entities added**", geo2, StringComparison.Ordinal);
        Assert.Contains("EF configurations added: **No**", geo2, StringComparison.Ordinal);
        Assert.Contains("Migration added: **No**", geo2, StringComparison.Ordinal);
        Assert.Contains("Regression tests added: **Yes, architecture regression only**", geo2, StringComparison.Ordinal);
        Assert.Contains("Backward compatible: **Yes as a design**", geo2, StringComparison.Ordinal);
        Assert.Contains("Production ready: **Design ready, implementation not production active**", geo2, StringComparison.Ordinal);
    }
}
