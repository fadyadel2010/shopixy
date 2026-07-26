namespace PointOfSale.Tests.Architecture;

public sealed class GeographicRuntimeAuthorityCertificationTests
{
    [Fact]
    public void Geo4RuntimeAuthorityDocument_MustExistAndRemainArchitectureOnly()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        Assert.Contains("Status: Runtime authority certification freeze pending approval", geo4, StringComparison.Ordinal);
        Assert.Contains("Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`", geo4, StringComparison.Ordinal);
        Assert.Contains("Runtime impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Backend impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Frontend impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Checkout impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Shipping runtime impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Carrier adapter impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("EF impact: none", geo4, StringComparison.Ordinal);
        Assert.Contains("Migration impact: none", geo4, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo4RuntimeMatrix_MustFreezeEveryRequiredRuntime()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        foreach (var requiredRuntime in new[]
                 {
                     "Platform Geographic Catalog",
                     "Carrier Geographic Mapping",
                     "Country Profiles",
                     "Localization",
                     "Aliases",
                     "Catalog Releases",
                     "Mapping Releases",
                     "Publication",
                     "Rollback",
                     "Search Projection",
                     "Provider Resolution",
                     "Checkout Geographic Resolution",
                     "Shipment Geographic Resolution",
                     "Warehouse Geographic Resolution",
                     "Affiliate Geographic Resolution",
                     "CommerceHub Geographic Resolution",
                     "Returns Geographic Resolution",
                     "Analytics Geographic Dimensions"
                 })
        {
            Assert.Contains(requiredRuntime, geo4, StringComparison.Ordinal);
        }

        Assert.Contains("Identity owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Authority and validation owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Persistence owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Publication owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Recovery and audit owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Projection, cache, sync owner", geo4, StringComparison.Ordinal);
        Assert.Contains("Runtime reads", geo4, StringComparison.Ordinal);
        Assert.Contains("Runtime writes", geo4, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo4RuntimeAuthority_MustRejectNonAuthoritativeSystems()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        Assert.Contains("Checkout, Shipment, Carrier Adapters, Search, Mongo, Redis, browser state", geo4, StringComparison.Ordinal);
        Assert.Contains("are not Geographic Authority", geo4, StringComparison.Ordinal);
        Assert.Contains("Redis, Mongo, Elasticsearch, offline packages, search indexes, autocomplete", geo4, StringComparison.Ordinal);
        Assert.Contains("are derivative only", geo4, StringComparison.Ordinal);
        Assert.Contains("Provider sync is evidence intake only", geo4, StringComparison.Ordinal);
        Assert.Contains("Provider API live lookup as mapping authority", geo4, StringComparison.Ordinal);
        Assert.Contains("Carrier adapters", geo4, StringComparison.Ordinal);
        Assert.Contains("never infer", geo4, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo4PublicationRecoveryProjectionAndCache_MustReEnterPublishedSqlTruth()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        Assert.Contains("Published Release", geo4, StringComparison.Ordinal);
        Assert.Contains("-> Runtime Resolution", geo4, StringComparison.Ordinal);
        Assert.Contains("-> Projection", geo4, StringComparison.Ordinal);
        Assert.Contains("-> Cache", geo4, StringComparison.Ordinal);
        Assert.Contains("-> Presentation", geo4, StringComparison.Ordinal);
        Assert.Contains("Drafts must never participate", geo4, StringComparison.Ordinal);

        Assert.Contains("Published Catalog", geo4, StringComparison.Ordinal);
        Assert.Contains("Published Mapping", geo4, StringComparison.Ordinal);
        Assert.Contains("SQL Truth", geo4, StringComparison.Ordinal);
        Assert.Contains("Recovery must never re-enter Redis, Mongo, Elasticsearch", geo4, StringComparison.Ordinal);
        Assert.Contains("Projection is derivative only.", geo4, StringComparison.Ordinal);
        Assert.Contains("Cache can accelerate reads and candidate presentation.", geo4, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo4FailClosedAndCrossDomainRules_MustRemainFrozen()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        foreach (var failure in new[]
                 {
                     "Unknown Place",
                     "Unknown Mapping",
                     "Unpublished Release",
                     "Stale Release",
                     "Broken Hierarchy",
                     "Broken Mapping",
                     "Missing Localization",
                     "Invalid Country Profile",
                     "Publication Conflict",
                     "Checksum Mismatch"
                 })
        {
            Assert.Contains(failure, geo4, StringComparison.Ordinal);
        }

        foreach (var domain in new[]
                 {
                     "Shipping",
                     "Checkout",
                     "Orders",
                     "Warehouses",
                     "Affiliate",
                     "CommerceHub",
                     "Returns",
                     "Analytics",
                     "Search"
                 })
        {
            Assert.Contains(domain, geo4, StringComparison.Ordinal);
        }

        Assert.Contains("Geographic remains the authority across all cross-domain interactions.", geo4, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo4ArtifactsAndContext_MustRecordRuntimeAuthorityFreeze()
    {
        var geo4 = CertificationSource.ReadRepoFile("docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md");

        Assert.Contains("Reviewed ONLY Geographic Runtime: **Yes**", geo4, StringComparison.Ordinal);
        Assert.Contains("Runtime files modified: **No**", geo4, StringComparison.Ordinal);
        Assert.Contains("Backend files modified: **No runtime backend files modified**", geo4, StringComparison.Ordinal);
        Assert.Contains("Frontend files modified: **No**", geo4, StringComparison.Ordinal);
        Assert.Contains("Architecture files added: **Yes**", geo4, StringComparison.Ordinal);
        Assert.Contains("Architecture tests added: **Yes, architecture regression only**", geo4, StringComparison.Ordinal);
        Assert.Contains("Authority violations found: **No current runtime authority violation found**", geo4, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening required: **No**", geo4, StringComparison.Ordinal);
        Assert.Contains("Recommend next phase: **GEO-5 - Geographic Runtime Implementation Plan**", geo4, StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("GEO-4", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
