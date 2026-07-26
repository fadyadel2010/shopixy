namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceListingAuthorityCertificationTests
{
    [Fact]
    public void ServiceSearchReads_MustRevalidateElasticsearchHitsAgainstCurrentSqlAuthority()
    {
        var searchService = ReadRepoFile("PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs");
        var searchInterface = ReadRepoFile("PointOfSale.Core/Interfaces/IMarketplaceSearchService.cs");

        Assert.Contains("LoadCurrentSqlListingAuthorityAsync", searchService, StringComparison.Ordinal);
        Assert.Contains("_context.Set<Service>().AsNoTracking().IgnoreQueryFilters()", searchService, StringComparison.Ordinal);
        Assert.Contains("join tenant in _context.Tenants.AsNoTracking().IgnoreQueryFilters()", searchService, StringComparison.Ordinal);
        Assert.Contains("join profile in _context.Set<MarketplaceProfile>().AsNoTracking().IgnoreQueryFilters()", searchService, StringComparison.Ordinal);
        Assert.Contains("!service.IsDeleted", searchService, StringComparison.Ordinal);
        Assert.Contains("service.IsActive", searchService, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", searchService, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", searchService, StringComparison.Ordinal);
        Assert.Contains("!profile.IsDeleted", searchService, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", searchService, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", searchService, StringComparison.Ordinal);
        Assert.Contains("Where(document => sqlAuthorityByServiceId.ContainsKey(document.ServiceId))", searchService, StringComparison.Ordinal);
        Assert.Contains("StartingPrice = authority.StartingPrice", searchService, StringComparison.Ordinal);
        Assert.Contains("ServiceImages = authority.ServiceImages", searchService, StringComparison.Ordinal);

        Assert.DoesNotContain("NEVER queries PostgreSQL", searchInterface, StringComparison.Ordinal);
        Assert.Contains("Elasticsearch supplies candidate discovery hits", searchInterface, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicServiceDetail_MustFailClosedWithoutCurrentSqlListingAuthority()
    {
        var catalogService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceCatalogService.cs");

        Assert.Contains("public async Task<Service?> GetServiceByIdAsync", catalogService, StringComparison.Ordinal);
        Assert.Contains("join tenant in _context.Tenants.IgnoreQueryFilters()", catalogService, StringComparison.Ordinal);
        Assert.Contains("join profile in _context.Set<MarketplaceProfile>().IgnoreQueryFilters()", catalogService, StringComparison.Ordinal);
        Assert.Contains("service.IsActive", catalogService, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", catalogService, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", catalogService, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", catalogService, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", catalogService, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceListingMutations_MustValidateDtoIntentBeforeSqlMutationAndEvents()
    {
        var catalogService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceCatalogService.cs");

        Assert.Contains("ValidateListingIntent(", catalogService, StringComparison.Ordinal);
        Assert.Contains("PRICE_INVALID", catalogService, StringComparison.Ordinal);
        Assert.Contains("PRICING_TYPE_REQUIRED", catalogService, StringComparison.Ordinal);
        Assert.Contains("CURRENCY_REQUIRED", catalogService, StringComparison.Ordinal);
        Assert.Contains("CURRENCY_INVALID", catalogService, StringComparison.Ordinal);
        Assert.Contains("NormalizeOptionalJsonArray(serviceImages", catalogService, StringComparison.Ordinal);
        Assert.Contains("IsSafeServiceMediaReference", catalogService, StringComparison.Ordinal);
        Assert.Contains("value.StartsWith(\"marketplace/services/\"", catalogService, StringComparison.Ordinal);
        Assert.Contains("Title = service.Title", catalogService, StringComparison.Ordinal);
        Assert.Contains("StartingPrice = service.StartingPrice", catalogService, StringComparison.Ordinal);
        Assert.Contains("ServiceImages = service.ServiceImages", catalogService, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceDiscoveryProjectionAndRebuild_MustConvergeFromCurrentSqlAuthorityOnly()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ServiceDiscoveryProjectionConsumer.cs");
        var driftJob = ReadRepoFile("PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs");

        Assert.Contains("LoadLiveServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("current SQL service/tenant/profile authority missing", consumer, StringComparison.Ordinal);
        Assert.Contains("DeleteServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", consumer, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", consumer, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", driftJob, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.ServiceMarketplace", driftJob, StringComparison.Ordinal);
        Assert.Contains("LoadLiveServiceSearchAuthorityAsync", driftJob, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", driftJob, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", driftJob, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceListingCertificationDocumentation_MustRecordS1AAuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-listing-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS S1-A Service Marketplace Listing Authority",
            "Identity findings",
            "Ownership findings",
            "Publication findings",
            "Pricing findings",
            "Category findings",
            "Media findings",
            "Discovery findings",
            "Projection findings",
            "Isolation findings",
            "Concurrency findings",
            "Historical findings",
            "Adversarial findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS S1-A Service Marketplace Listing Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS S1-A Service Marketplace Listing Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS S1-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS S1-A Service Marketplace Listing Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS S1-A Service Marketplace Listing Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
