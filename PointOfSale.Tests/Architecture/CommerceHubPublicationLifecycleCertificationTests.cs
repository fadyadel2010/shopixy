namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPublicationLifecycleCertificationTests
{
    [Fact]
    public void MerchantLifecycle_MustPreserveIntentOwnershipAndAllowWithdrawalAfterVariantRemoval()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var frontend = ReadRepoFile("PointOfSale.Web/src/pages/CommerceHubSettings.jsx");

        var unpublish = ExtractTypeBody(handlers, "UnpublishFromCommerceHubHandler");

        Assert.Contains("CommerceHubTenantIsolationGuard.EnsureTenantAccess", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListing.Create", handlers, StringComparison.Ordinal);
        Assert.Contains("listing.Publish", handlers, StringComparison.Ordinal);
        Assert.Contains("listing.Update", handlers, StringComparison.Ordinal);
        Assert.Contains("listing.Unpublish", unpublish, StringComparison.Ordinal);
        Assert.Contains("_dbContext.CommerceHubListings", unpublish, StringComparison.Ordinal);
        Assert.Contains(".IgnoreQueryFilters()", unpublish, StringComparison.Ordinal);
        Assert.Contains("listing == null && variant == null", unpublish, StringComparison.Ordinal);
        Assert.DoesNotContain("x.IsActive &&", unpublish, StringComparison.Ordinal);
        Assert.Contains("RestorePublicationOnActivate = false", listing, StringComparison.Ordinal);
        Assert.Contains("ClearGovernanceRestoreState", listing, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"publish\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"update\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpDelete(\"unpublish\")]", controller, StringComparison.Ordinal);
        Assert.Contains("handlePublish", frontend, StringComparison.Ordinal);
        Assert.Contains("handleUnpublish", frontend, StringComparison.Ordinal);
        Assert.Contains("priceOverrideMutationVersion", frontend, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityProjectionSearchAndRecovery_MustRecomposeCurrentSqlAuthority()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projection = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", overlay, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", query, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", cart, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage()", checkout, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", cart, StringComparison.Ordinal);
        Assert.Contains("_priceAuthority.ResolveAsync(variant, listing, cancellationToken)", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationLifecycleCertificationDocumentation_MustRecordPassFourteenOne()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-publication-lifecycle-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 14.1",
            "1. Lifecycle Inventory",
            "2. Frontend Findings",
            "3. Publication Findings",
            "4. Ownership Findings",
            "5. Category Findings",
            "6. Attribute Findings",
            "7. Pricing Findings",
            "8. Variant Findings",
            "9. Visibility Findings",
            "10. Search And Discovery Findings",
            "11. Recovery And Replay Findings",
            "12. Governance Findings",
            "13. Chaos Findings",
            "14. Monitoring Findings",
            "15. Runtime Hardening Performed",
            "16. Regression Freeze Added",
            "17. Architecture Updates",
            "18. Remaining Debt"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 14.1 CommerceHub Product Publication Lifecycle Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 14.1 CommerceHub Product Publication Lifecycle Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 14.1", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 14.1 CommerceHub Product Publication Lifecycle Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 14.1 CommerceHub Product Publication Lifecycle Certification Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ExtractTypeBody(string source, string typeName)
    {
        var typeIndex = source.IndexOf($"class {typeName}", StringComparison.Ordinal);
        Assert.True(typeIndex >= 0, $"Could not find type {typeName}.");

        var bodyStart = source.IndexOf('{', typeIndex);
        Assert.True(bodyStart >= 0, $"Could not find body for type {typeName}.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(bodyStart, index - bodyStart + 1);
            }
        }

        throw new InvalidOperationException($"Could not parse body for type {typeName}.");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
