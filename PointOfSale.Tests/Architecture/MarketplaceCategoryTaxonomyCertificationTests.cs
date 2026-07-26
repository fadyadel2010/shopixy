namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceCategoryTaxonomyCertificationTests
{
    [Fact]
    public void MerchantCategorySelectionAndAttributes_MustFailClosedAgainstLiveSqlTaxonomy()
    {
        var categoriesController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCategoriesController.cs");
        var attributeService = ReadRepoFile("PointOfSale.API/CommerceHub/Services/CommerceHubAttributeService.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");

        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", categoriesController, StringComparison.Ordinal);
        Assert.Contains("liveCategoryIds.Contains(c.Id)", categoriesController, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", categoriesController, StringComparison.Ordinal);
        Assert.Contains("return NotFound();", categoriesController, StringComparison.Ordinal);

        Assert.Contains("CommerceHubCategoryLineageAuthority.IsLiveAsync", attributeService, StringComparison.Ordinal);
        Assert.Contains("CommerceHub category is not currently available for attribute validation.", attributeService, StringComparison.Ordinal);
        Assert.Contains("ValidateAndNormalizeAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCommerceHubCategoryAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("BuildSchemaSnapshotAsync", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceCategoryLineage_MustStaySqlAuthoritativeAndResurrectionResistant()
    {
        var lineage = ReadRepoFile("PointOfSale.Infrastructure/Reliability/CommerceHubCategoryLineageAuthority.cs");
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projectionConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var searchHealth = ReadRepoFile("PointOfSale.API/HealthChecks/CommerceHubSearchSyncHealthCheck.cs");

        Assert.Contains("item.Id == currentCategoryId && item.IsActive", lineage, StringComparison.Ordinal);
        Assert.Contains("!visited.Add(currentCategoryId)", lineage, StringComparison.Ordinal);
        Assert.Contains("category.ParentId.Value == category.Id", lineage, StringComparison.Ordinal);

        Assert.Contains("SqlAuthorityOverlayKind.Category", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("category == null", overlay, StringComparison.Ordinal);
        Assert.Contains("document.CategoryPath = category.PathText", overlay, StringComparison.Ordinal);

        Assert.Contains("ResolveCommerceHubCategoryMetadataAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projectionConsumer, StringComparison.Ordinal);

        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindex, StringComparison.Ordinal);

        Assert.Contains("LoadFacetDefinitionsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("MapSqlVisibleFacetsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadCategoryFacetMetadataAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterAutocompleteCategoryDtosAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("IsLiveCommerceHubCategoryAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", searchHealth, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceTaxonomyCertificationDocumentation_MustRecordPassTwelveTwo()
    {
        var certification = ReadRepoFile("docs/architecture/marketplace-category-taxonomy-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.2",
            "1. Taxonomy Inventory Findings",
            "2. Category Mapping Findings",
            "3. Category Lineage Findings",
            "4. Merchant Category Selection Findings",
            "5. Search And Filter Findings",
            "6. Attribute Dependency Findings",
            "7. Recovery And Rebuild Findings",
            "8. Search Index Findings",
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

        Assert.Contains("PASS 12.2 Marketplace Category Mapping & Taxonomy Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.2 Marketplace Category Mapping & Taxonomy Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.2", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.2 Marketplace Category Mapping & Taxonomy Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.2 Marketplace Category Mapping & Taxonomy Authority Freeze", codexRules, StringComparison.Ordinal);
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
