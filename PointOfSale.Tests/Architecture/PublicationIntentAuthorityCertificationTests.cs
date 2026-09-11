namespace PointOfSale.Tests.Architecture;

public sealed class PublicationIntentAuthorityCertificationTests
{
    [Fact]
    public void MerchantPublicationMutationPaths_MustKeepIntentSeparateFromVisibilityAuthority()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var eligibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicationEligibilityPolicy.cs");
        var listing = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubListing.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubListingConfiguration.cs");

        var publishHandler = ExtractTypeBody(handlers, "PublishToCommerceHubHandler");
        var updateHandler = ExtractTypeBody(handlers, "UpdateCommerceHubListingHandler");
        var unpublishHandler = ExtractTypeBody(handlers, "UnpublishFromCommerceHubHandler");

        Assert.Contains("EnsureVendorListingAllowedAsync", publishHandler, StringComparison.Ordinal);
        Assert.Contains("EnsureTenantHasActiveShippingAsync", publishHandler, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCommerceHubCategoryAsync", publishHandler, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", publishHandler, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPublicationEligibilityPolicy.GetBlockingMessage", publishHandler, StringComparison.Ordinal);
        Assert.Contains("variant.Product.PublishStatus != PublishStatus.Published", eligibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListing.Create", publishHandler, StringComparison.Ordinal);

        Assert.Contains("EnsureVendorListingAllowedAsync", updateHandler, StringComparison.Ordinal);
        Assert.Contains("EnsureLiveCommerceHubCategoryAsync", updateHandler, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", updateHandler, StringComparison.Ordinal);
        Assert.Contains("listing.Update", updateHandler, StringComparison.Ordinal);

        Assert.Contains("CommerceHubTenantIsolationGuard.EnsureTenantAccess", unpublishHandler, StringComparison.Ordinal);
        Assert.Contains("listing.Unpublish", unpublishHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVendorListingAllowedAsync", unpublishHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("IUserGovernanceService", unpublishHandler, StringComparison.Ordinal);

        Assert.Contains("RestorePublicationOnActivate = false", listing, StringComparison.Ordinal);
        Assert.Contains("ClearGovernanceRestoreState", listing, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubListings_CommerceHubPrice_Positive", configuration, StringComparison.Ordinal);
        Assert.Contains("UQ_CommerceHubListings_TenantId_VariantId", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityAndRecoveryPaths_MustRemainSqlAuthoritativeAndResurrectionResistant()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var projectionConsumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");
        var queryService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", overlay, StringComparison.Ordinal);
        Assert.Contains("listing == null || !listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("item.IsActive", overlay, StringComparison.Ordinal);
        Assert.Contains("item.PublishStatus == PublishStatus.Published", overlay, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", overlay, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("LoadSqlCommerceHubListingAuthorityAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", projectionConsumer, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync", projectionConsumer, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("TenantId", autoHealing, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", autoHealing, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationIntentCertificationDocumentation_MustRecordPassTwelveOne()
    {
        var certification = ReadRepoFile("docs/architecture/publication-intent-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 12.1",
            "1. Publication Inventory Findings",
            "2. Lifecycle Findings",
            "3. Merchant Intent Findings",
            "4. Governance Findings",
            "5. Product Authority Findings",
            "6. Ownership Findings",
            "7. Replay And Recovery Findings",
            "8. Search And Discovery Findings",
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

        Assert.Contains("PASS 12.1 Merchant Publication Intent Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 12.1 Merchant Publication Intent Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 12.1", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 12.1 Merchant Publication Intent Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 12.1 Merchant Publication Intent Authority Freeze", codexRules, StringComparison.Ordinal);
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
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not parse body for type {typeName}.");
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
