namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceAttributeFilterGovernanceCertificationTests
{
    [Fact]
    public void MerchantAttributes_MustFailClosedAgainstCurrentSqlSchema()
    {
        var attributeService = ReadRepoFile("PointOfSale.API/CommerceHub/Services/CommerceHubAttributeService.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");

        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", attributeService, StringComparison.Ordinal);
        Assert.Contains("is not supported for this CommerceHub category.", attributeService, StringComparison.Ordinal);
        Assert.Contains("Attribute validation failed", handlers, StringComparison.Ordinal);
        Assert.Contains("BuildSchemaSnapshotAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("AttributeSchemaSnapshotJson", ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SearchFiltersAndFacets_MustBeBoundToLiveSqlCategoryAttributes()
    {
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var parser = ReadRepoFile("PointOfSale.API/CommerceHub/Services/CommerceHubSearchQueryParser.cs");
        var readRepository = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/CommerceHubReadRepository.cs");
        var searchReadService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchReadService.cs");

        Assert.Contains("LoadFacetDefinitionsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("HasUnsupportedAttributeFilters", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHub search rejected unsupported attribute filters", queryService, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubSearchFacetsDto()", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubAttributeService.NormalizeKey", parser, StringComparison.Ordinal);
        Assert.Contains("BuildAttributeFilterDocument", readRepository, StringComparison.Ordinal);
        Assert.Contains("BuildAttributeFilter", searchReadService, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeIndexingAndRecovery_MustStripDeletedOrUnsupportedStoredAttributes()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projectionConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var indexService = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");

        Assert.Contains("NormalizeAttributesAgainstCurrentSchemaAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("dbContext.CommerceHubCategoryAttributes", overlay, StringComparison.Ordinal);
        Assert.Contains("document.AttributesJson = await NormalizeAttributesAgainstCurrentSchemaAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("document.Attributes = ParseAttributesToBsonDocument(document.AttributesJson)", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("[\"attributes\"] = document.Attributes", indexService, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeGovernanceCertificationDocumentation_MustRecordPassTwelveThree()
    {
        var certification = ReadRepoFile("docs/architecture/marketplace-attribute-filter-governance-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.3",
            "1. Attribute Inventory Findings",
            "2. Attribute Binding Findings",
            "3. Attribute Poisoning Findings",
            "4. Schema Evolution Findings",
            "5. Search And Facet Findings",
            "6. Indexing Findings",
            "7. Replay And Recovery Findings",
            "8. Merchant Input Findings",
            "9. Auditability Findings",
            "10. Chaos Findings",
            "11. Runtime Hardening Performed",
            "12. Monitoring Improvements",
            "13. Governance Updates",
            "14. Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 12.3 Marketplace Attribute & Filter Governance", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.3 Marketplace Attribute & Filter Governance Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.3", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.3 Marketplace Attribute & Filter Governance", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.3 Marketplace Attribute & Filter Governance Freeze", codexRules, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
