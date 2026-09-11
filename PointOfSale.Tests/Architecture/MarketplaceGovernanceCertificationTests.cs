namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceGovernanceCertificationTests
{
    [Fact]
    public void MarketplaceProductProjection_MustRequireSqlListingAuthority()
    {
        var source = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));

        Assert.Contains("LoadLiveMarketplaceListingAsync", source, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", source, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", source, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage", source, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepageDiscovery_MustFenceReplayAndScrubStaleCuratedReferences()
    {
        AssertSourceContains(
            InfrastructurePath("Consumers", "Marketplace", "HomepageProjectionConsumer.cs"),
            "ProjectionConsumerEventFence.TryAcquireAsync",
            "duplicate or active replay collapsed before side effects",
            "fence.CompleteAsync");

        AssertSourceContains(
            InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"),
            "SanitizeSectionsAsync",
            "LoadLiveListingIdsAsync",
            "CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync",
            "RewriteGuidArray",
            "ShouldSkipCuratedSection");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Services", "SuperAdminHomepageSectionManagementService.cs"),
            "WherePubliclyVisibleLineage",
            "CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync",
            "FilterVisibleListingIdsAsync",
            "tenant.Status == TenantStatus.Active",
            "HOMEPAGE_SECTION_REFERENCE_INVALID");

        AssertSourceContains(
            ApiPath("Services", "Homepage", "HomepageSectionReadService.cs"),
            "SanitizeDiscoveryReferencesAsync",
            "WherePubliclyVisibleLineage",
            "CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync",
            "FilterVisibleListingIdsAsync",
            "tenant.Status == TenantStatus.Active");

        AssertSourceContains(
            ApiPath("Controllers", "MarketplaceReadController.cs"),
            "FilterPublicReadAsync",
            "MarketplaceHomepageReadModelDocument");

        AssertSourceContains(
            InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"),
            "FilterPublicReadAsync",
            "SanitizeProjectedSectionsAsync");
    }

    [Fact]
    public void CommerceHubSearch_MustUseTenantScopedSearchIdentity()
    {
        AssertSourceContains(
            InfrastructurePath("Elasticsearch", "Documents", "CommerceHubProductSearchDocument.cs"),
            "GetDocumentId() => $\"{TenantId:N}:{Id:N}\"",
            "GetLegacyDocumentId");

        AssertSourceContains(
            InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchIndexService.cs"),
            "CommerceHubSearchDocumentIdentity",
            "DeleteAsync(",
            "identity.GetDocumentId()",
            "identity.GetLegacyDocumentId()");

        AssertSourceContains(
            InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchReadService.cs"),
            "LastIndexOf(':')",
            "scopedListingId");
    }

    [Fact]
    public void MarketplacePublicMediaAndReviewRanking_MustRecomposeSqlAuthority()
    {
        var overlay = File.ReadAllText(
            InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchSqlAuthorityOverlay.cs"));
        var marketplaceProjection = File.ReadAllText(
            InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var commerceHubProjection = File.ReadAllText(
            InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));

        Assert.Contains("document.Image = ResolveImage(product, variant)", overlay, StringComparison.Ordinal);
        Assert.Contains("document.ReviewCount = approvedReviewRatings.Count", overlay, StringComparison.Ordinal);
        Assert.Contains("review.Status == ProductReviewStatus.Approved", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.ImageUrl", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("product.Images", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("existingImage", overlay, StringComparison.Ordinal);

        Assert.DoesNotContain("defaultVariant?.ImageUrl", marketplaceProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("SplitImageList(product.Images)", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFirstLegacyImage", commerceHubProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubProductDetail_MustRecomposeSqlVisibilityAndAvoidLegacyImageFallback()
    {
        var queryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubQueryService.cs"));

        Assert.Contains("var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(", queryService, StringComparison.Ordinal);
        Assert.Contains("return MapDetail(sqlAuthority, product);", queryService, StringComparison.Ordinal);
        Assert.Contains("CategoryName = listing.CategoryName", queryService, StringComparison.Ordinal);
        Assert.DoesNotContain("product.CategoryName ?? listing.CategoryName", queryService, StringComparison.Ordinal);
        Assert.DoesNotContain("return variant.ImageUrl;", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubBrowseAndSearchCards_MustUseSqlAuthorityBeforeServing()
    {
        var queryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubQueryService.cs"));
        var marketplaceReadController = File.ReadAllText(ApiPath("Controllers", "MarketplaceReadController.cs"));

        Assert.Contains("_repository.SearchPublishedProductsAsync(request, cancellationToken)", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleListingItemsAsync(result.Items, cancellationToken)", queryService, StringComparison.Ordinal);
        Assert.Contains("from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()", queryService, StringComparison.Ordinal);
        Assert.Contains("profile.IsKycVerified &&", queryService, StringComparison.Ordinal);
        Assert.Contains("profile.IsMarketplaceApproved) &&", queryService, StringComparison.Ordinal);
        Assert.Contains("liveCategoryIds.Contains(listing.CommerceHubCategoryId)", queryService, StringComparison.Ordinal);
        Assert.Contains("from stock in _dbContext.ProductVariantStocks.AsNoTracking().IgnoreQueryFilters()", queryService, StringComparison.Ordinal);

        Assert.Contains("GetVisibilityVersionTokenAsync", marketplaceReadController, StringComparison.Ordinal);
        Assert.Contains("GetDistinctVendorIdsAsync", marketplaceReadController, StringComparison.Ordinal);
        Assert.Contains("GetSuppressedVendorIdsAsync", marketplaceReadController, StringComparison.Ordinal);
        Assert.Contains("IsVendorSuppressedAsync", marketplaceReadController, StringComparison.Ordinal);
        Assert.Contains("FilterVisibleListingIdsAsync", marketplaceReadController, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubVendorPublicationCards_MustCarryCurrentTenantAuthority()
    {
        var controller = File.ReadAllText(ApiPath("Controllers", "AdminCommerceHubController.cs"));
        var dto = File.ReadAllText(ApiPath("DTOs", "CommerceHub", "CommerceHubAdminDtos.cs"));
        var frontend = File.ReadAllText(WebPath("src", "pages", "CommerceHubSettings.jsx"));

        Assert.Contains("public Guid TenantId { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("TenantId = p.TenantId,", controller, StringComparison.Ordinal);
        Assert.Contains("deriveCurrentVendorScopedArray(productsRes || [], currentUser)", frontend, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyMarketplaceServiceReindex_MustNotLetTenantAdminsMutateGlobalSearchIndex()
    {
        var controller = File.ReadAllText(ApiPath("Controllers", "MarketplaceController.cs"));

        Assert.Contains("[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]", controller, StringComparison.Ordinal);
        Assert.Contains("ReindexServicesGlobally", controller, StringComparison.Ordinal);
        Assert.Contains("ReindexTenantServicesAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Requires current SQL super-admin platform authority.", controller, StringComparison.Ordinal);
        Assert.Contains("s.TenantId == tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("s.IsActive", controller, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", controller, StringComparison.Ordinal);
        Assert.Contains("!s.IsDeleted", controller, StringComparison.Ordinal);
        Assert.Contains("s.IsActive", controller, StringComparison.Ordinal);
        Assert.Contains("GLOBAL_SEARCH_INDEX_UNAVAILABLE", controller, StringComparison.Ordinal);
        Assert.Contains("LoadExistingTenantServiceSearchIdsAsync", controller, StringComparison.Ordinal);
        Assert.Contains(".Scroll(scrollTimeout)", controller, StringComparison.Ordinal);
        Assert.Contains(".DeleteMany(batch.Select(serviceId => new MarketplaceServiceDocument", controller, StringComparison.Ordinal);

        var globalBranch = controller[
            controller.IndexOf("Super admin requested global marketplace services reindex", StringComparison.Ordinal)..];
        Assert.Contains("_elasticClient.Indices.DeleteAsync(indexName", globalBranch, StringComparison.Ordinal);

        var tenantBranch = controller[
            controller.IndexOf("private async Task<IActionResult> ReindexTenantServicesAsync", StringComparison.Ordinal)..
            controller.IndexOf("private Guid GetUserId()", StringComparison.Ordinal)];
        Assert.DoesNotContain("_elasticClient.Indices.DeleteAsync(indexName", tenantBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("Indices.CreateAsync", tenantBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyMarketplaceServiceSearchRecovery_MustRecomposeCurrentSqlAuthority()
    {
        var consumer = File.ReadAllText(InfrastructurePath("Consumers", "ServiceDiscoveryProjectionConsumer.cs"));
        var driftJob = File.ReadAllText(InfrastructurePath("Jobs", "ElasticsearchDriftReconciliationJob.cs"));
        var overlay = File.ReadAllText(InfrastructurePath("Reliability", "SqlAuthorityOverlay.cs"));

        Assert.Contains("LoadLiveServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("LoadLiveServiceDocumentsByProviderAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("DeleteServiceDocumentAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", consumer, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", consumer, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", consumer, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("Title = message.Title", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderDisplayName = profile?.DisplayName ?? \"Unknown\"", consumer, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireGlobalSqlAuthority", driftJob, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.ServiceMarketplace", driftJob, StringComparison.Ordinal);
        Assert.Contains("LoadLiveServiceSearchAuthorityAsync", driftJob, StringComparison.Ordinal);
        Assert.Contains("tenant.Status == TenantStatus.Active", driftJob, StringComparison.Ordinal);
        Assert.Contains("tenant.DeletedAtUtc == null", driftJob, StringComparison.Ordinal);
        Assert.Contains("profile.IsActive", driftJob, StringComparison.Ordinal);
        Assert.Contains("profile.TenantId == service.TenantId", driftJob, StringComparison.Ordinal);
        Assert.Contains("ServiceMarketplace", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceGovernanceDocumentation_MustRecordFindingsAndRemainingDebt()
    {
        var certification = File.ReadAllText(DocsPath("architecture", "marketplace-governance-certification-findings.md"));

        foreach (var marker in new[]
        {
            "SUPERADMIN-CON-01 Phase 2",
            "Boundaries Audited",
            "Runtime Hardening Added",
            "Replay And Rebuild Guarantees",
            "Moderation And Recommendation Governance",
            "Operational Auditability Findings",
            "Remaining Marketplace Risk Debt",
            "Regression Freeze Invariants",
            "CommerceHubListings",
            "tenant-scoped"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static void AssertSourceContains(string path, params string[] markers)
    {
        var source = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string WebPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Web", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
