using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Tests.Architecture;

public class GlobalReliabilityInfrastructureExtractionTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine(new[] { Root, "PointOfSale.Infrastructure" }.Concat(parts).ToArray());

    private static string ApiPath(params string[] parts) =>
        Path.Combine(new[] { Root, "PointOfSale.API" }.Concat(parts).ToArray());

    [Fact]
    public void ProjectionEventFence_IsExtractedAndCompatibilityFacadeDelegatesToIt()
    {
        var fence = File.ReadAllText(InfrastructurePath("Reliability", "ProjectionEventFence.cs"));
        var facade = File.ReadAllText(InfrastructurePath("Consumers", "ProjectionConsumerEventFence.cs"));

        Assert.Contains("public sealed class ProjectionEventFence", fence, StringComparison.Ordinal);
        Assert.Contains("pg_try_advisory_lock", fence, StringComparison.Ordinal);
        Assert.Contains("ProcessedEvents", fence, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlAuthorityOverlay_ExposesReusableOverlayResultAndSearchOverlayUsesIt()
    {
        var overlayInfrastructure = File.ReadAllText(InfrastructurePath("Reliability", "SqlAuthorityOverlay.cs"));
        var searchOverlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "ProductSearchSqlAuthorityOverlay.cs"));
        var commerceHubSearchOverlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchSqlAuthorityOverlay.cs"));

        Assert.Contains("ISqlAuthorityOverlay<TProjection>", overlayInfrastructure, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Variant", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Pricing", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Stock", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Media", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Category", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Promotion", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("ResolveLiveCategoryNameAsync", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("visited.Add(currentCategoryId)", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("category.IsDeleted", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("ThenInclude(stock => stock.Warehouse)", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("SumCurrentVariantStock", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("stock.Warehouse.IsActive", searchOverlay, StringComparison.Ordinal);

        Assert.Contains("ISqlAuthorityOverlay<CommerceHubProductDocument>", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Category", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("dbContext.CommerceHubListings", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("dbContext.ProductVariants", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains(".WherePubliclyVisibleLineage(tenantId)", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", commerceHubSearchOverlay, StringComparison.Ordinal);
        Assert.Contains("return null;", commerceHubSearchOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchRebuildAndRecoveryPaths_MustNotReindexFromMongoWithoutSqlAuthorityOverlay()
    {
        var storefrontReindex = File.ReadAllText(ApiPath("Controllers", "SearchReindexController.cs"));
        var commerceHubReindex = File.ReadAllText(InfrastructurePath("Jobs", "CommerceHubReindexService.cs"));
        var commerceHubAutoHealing = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubAutoHealingService.cs"));
        var commerceHubAnalytics = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubSearchAnalyticsBackgroundService.cs"));
        var commerceHubConsumer = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontReindex, StringComparison.Ordinal);
        Assert.Contains("new ProductDocument", storefrontReindex, StringComparison.Ordinal);
        Assert.Contains("_indexService.DeleteAsync(tenantId, productId", storefrontReindex, StringComparison.Ordinal);
        Assert.Contains("!sqlProjection.IsActive || !sqlProjection.IsOnlineVisible", storefrontReindex, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync(document.TenantId, document.Id", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", commerceHubReindex, StringComparison.Ordinal);
        Assert.Contains(".WherePubliclyVisibleLineage()", commerceHubReindex, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubAutoHealing, StringComparison.Ordinal);
        Assert.Contains("new CommerceHubProductDocument", commerceHubAutoHealing, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync(", commerceHubAutoHealing, StringComparison.Ordinal);
        Assert.Contains("queuedItem.TenantId.Value", commerceHubAutoHealing, StringComparison.Ordinal);
        Assert.Contains("document.TenantId,", commerceHubAutoHealing, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubAnalytics, StringComparison.Ordinal);
        Assert.Contains("indexService.DeleteAsync(", commerceHubAnalytics, StringComparison.Ordinal);
        Assert.Contains("document.TenantId,", commerceHubAnalytics, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchDocumentMapper.Map(sqlAuthority)", commerceHubAnalytics, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubSearchDocumentMapper.Map(document)", commerceHubAnalytics, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync(document.Id", commerceHubAnalytics, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", commerceHubConsumer, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync(document.TenantId, document.Id", commerceHubConsumer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubAutocompleteAndSuggestions_MustRevalidateSqlAuthorityBeforeReturningCachedOrProjectedResults()
    {
        var queryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubQueryService.cs"));

        Assert.Contains("RevalidateSuggestionsResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RevalidateAutocompleteResponseAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleSuggestionProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("RecomposeVisibleAutocompleteProductsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadPubliclySuppressedVendorIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadSqlTenantIdsByListingIdAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("FilterLiveSuggestionTermsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadSuspendedVendorIdsAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("LoadGovernanceHiddenVendorIdsAsync(", queryService, StringComparison.Ordinal);
        Assert.Contains("IsLiveCommerceHubCategoryAsync", queryService, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", queryService, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterSuspendedSuggestionProductsAsync", queryService, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterSuspendedAutocompleteProductsAsync", queryService, StringComparison.Ordinal);
        Assert.DoesNotContain("request.ExcludedTenantIds = await LoadGovernanceHiddenVendorIdsAsync", queryService, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalMigratedConsumers_UseExtractedProjectionFence()
    {
        var media = File.ReadAllText(InfrastructurePath("Consumers", "ProductMediaChangedEventConsumer.cs"));
        var stock = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockChangedConsumer.cs"));
        var search = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));
        var createdSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductSearchIndexConsumer.cs"));
        var updatedSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductUpdatedSearchIndexConsumer.cs"));
        var deletedSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductDeletedSearchIndexConsumer.cs"));
        var cache = File.ReadAllText(InfrastructurePath("Consumers", "ProductCacheInvalidationConsumer.cs"));

        Assert.Contains("ProjectionEventFence.TryAcquireAsync", media, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", stock, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", search, StringComparison.Ordinal);
        Assert.Contains("fence.CompleteAsync", search, StringComparison.Ordinal);
        Assert.Contains("ProductStockConsumerName", search, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", createdSearch, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", createdSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", createdSearch, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", updatedSearch, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", updatedSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", updatedSearch, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductDeletedEvent>", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductArchivedEvent>", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("_indexService.IndexAsync", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("_indexService.DeleteAsync", deletedSearch, StringComparison.Ordinal);
        Assert.Contains("ProductCreatedConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ProductUpdatedConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ProductDeletedConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ProductStockConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ProductPriceConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ProductPublicationConsumerName", cache, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlProductSlugAsync", cache, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", cache, StringComparison.Ordinal);
        Assert.Contains("fence.CompleteAsync", cache, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", cache, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorityValidationAndTombstoneGuards_AreReusableByHighRiskConsumers()
    {
        var variant = File.ReadAllText(InfrastructurePath("Consumers", "ProductVariantProjectionConsumer.cs"));
        var media = File.ReadAllText(InfrastructurePath("Consumers", "ProductMediaChangedEventConsumer.cs"));
        var commerceHub = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var promotion = File.ReadAllText(InfrastructurePath("Consumers", "PromotionProjectionConsumer.cs"));

        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", variant, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersionAndTimestamp", variant, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", variant, StringComparison.Ordinal);
        Assert.Contains("ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent", variant, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", media, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.IsIncomingStockStale", commerceHub, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", commerceHub, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", promotion, StringComparison.Ordinal);
        Assert.Contains("LoadLiveSqlPromotionAuthorityAsync", promotion, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryAvailabilityAndStockFanout_MustUseCurrentStockOwnerLineage()
    {
        var stockRepository = File.ReadAllText(InfrastructurePath("Data", "Repositories", "ProductVariantStockRepository.cs"));
        var mutationSupport = File.ReadAllText(ApiPath("Services", "Inventory", "ProductVariantStockMutationSupport.cs"));
        var commerceHubInventory = File.ReadAllText(ApiPath("Services", "CommerceHub", "InventoryConsistencyService.cs"));
        var variantReadRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductVariantReadRepository.cs"));
        var cartService = File.ReadAllText(ApiPath("Services", "Cart", "CartService.cs"));
        var affiliateIntelligence = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));
        var projectionCompensation = File.ReadAllText(InfrastructurePath("Eventing", "ProjectionFailureCompensation.cs"));

        Assert.Contains("IsVariantReservableForInventoryAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", stockRepository, StringComparison.Ordinal);
        Assert.Contains(".WhereInventoryReservableLineage(tenantId)", stockRepository, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", mutationSupport, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", commerceHubInventory, StringComparison.Ordinal);
        Assert.Contains(".WherePubliclyVisibleLineage(tenantId)", variantReadRepository, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", variantReadRepository, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", cartService, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(message.TenantId)", projectionCompensation, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductPublicationVisibility_MustTombstoneDeleteArchiveAndRecomposeCreateUpdateFromSql()
    {
        var created = File.ReadAllText(InfrastructurePath("Consumers", "ProductCreatedEventConsumer.cs"));
        var updated = File.ReadAllText(InfrastructurePath("Consumers", "ProductUpdatedEventConsumer.cs"));
        var lifecycle = File.ReadAllText(InfrastructurePath("Consumers", "ProductVisibilityLifecycleProjectionConsumer.cs"));
        var productProjectionWriter = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductSqlAuthorityProjectionWriter.cs"));
        var productMutationBoundary = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "StorefrontProductProjectionMutationBoundary.cs"));
        var repository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductReadRepository.cs"));
        var mongoRegistrations = File.ReadAllText(InfrastructurePath("MongoDB", "MongoDbServiceCollectionExtensions.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var productsController = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));
        var productSearchOverlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "ProductSearchSqlAuthorityOverlay.cs"));
        var commerceHub = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var commerceHubSearchOverlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchSqlAuthorityOverlay.cs"));
        var affiliatePublic = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var affiliateIntelligence = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));

        Assert.Contains("public sealed class ProductSqlAuthorityProjectionWriter", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("IStorefrontProductProjectionMutationBoundary", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("_mutationBoundary.ApplyAsync", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("ProductSqlAuthorityProjectionWriteDisposition", productProjectionWriter, StringComparison.Ordinal);
        Assert.Contains("public sealed class StorefrontProductProjectionMutationBoundary", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("ReviewedFullDocumentUpsert", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("ReviewedVisibilityTombstone", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("_repository.UpsertAsync", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("_repository.TombstoneIfFreshAsync", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_PRODUCT_UPSERT_REQUIRES_VISIBLE_SQL_PROJECTION", productMutationBoundary, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStorefrontProductProjectionMutationBoundary, StorefrontProductProjectionMutationBoundary>()", mongoRegistrations, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IProductSqlAuthorityProjectionWriter, ProductSqlAuthorityProjectionWriter>()", mongoRegistrations, StringComparison.Ordinal);

        Assert.Contains("IProductSqlAuthorityProjectionWriter", created, StringComparison.Ordinal);
        Assert.Contains("_projectionWriter.ApplyAsync", created, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", created, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductSearchSqlAuthorityOverlay.ApplyAsync", created, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", updated, StringComparison.Ordinal);
        Assert.Contains("_projectionWriter.ApplyAsync", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductSearchSqlAuthorityOverlay.ApplyAsync", updated, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductDeletedEvent>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductArchivedEvent>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", lifecycle, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", lifecycle, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Tombstone", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IProductSqlAuthorityProjectionWriter", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_projectionWriter.ApplyAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("x.AddConsumer<ProductVisibilityLifecycleProjectionConsumer>();", program, StringComparison.Ordinal);
        Assert.Contains("new ProductPublishStatusChangedEvent", productsController, StringComparison.Ordinal);
        Assert.Contains("PublishStatus = product.PublishStatus.ToString()", productsController, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", productSearchOverlay, StringComparison.Ordinal);

        Assert.Contains("Task<bool> TombstoneIfFreshAsync", repository, StringComparison.Ordinal);
        Assert.Contains(".Set(p => p.IsActive, false)", repository, StringComparison.Ordinal);
        Assert.Contains(".Set(p => p.IsOnlineVisible, false)", repository, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", commerceHub, StringComparison.Ordinal);
        Assert.Contains("ReconcileListingsByProductPublicationAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("ProductLifecycleConsumerName", commerceHub, StringComparison.Ordinal);
        Assert.Contains("UpdateListingPublicationVisibilityIfFreshAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("PublishStatus.Published", commerceHubSearchOverlay, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductDeletedEvent>", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductArchivedEvent>", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("SyncProductLifecycleVisibilityWithFenceAsync", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("RefreshPublicAffiliateStoreAsync", affiliatePublic, StringComparison.Ordinal);

        Assert.Contains("IConsumer<ProductDeletedEvent>", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductArchivedEvent>", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("IsProductVisibleInSqlAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("DeleteProductIntelligenceFromSqlLifecycleAsync", affiliateIntelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontProjectionCompletenessAndGovernance_MustKeepUnitsAuthoritativeAndBlockRawBypasses()
    {
        var productDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));
        var visibilityAuthority = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductStorefrontVisibilityAuthority.cs"));
        var overlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "ProductSearchSqlAuthorityOverlay.cs"));
        var promotion = File.ReadAllText(InfrastructurePath("Consumers", "PromotionProjectionConsumer.cs"));
        var writer = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductSqlAuthorityProjectionWriter.cs"));
        var mutationBoundary = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "StorefrontProductProjectionMutationBoundary.cs"));
        var searchMapper = File.ReadAllText(InfrastructurePath("Elasticsearch", "Documents", "ProductSearchDocumentMapper.cs"));
        var storefrontProductService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontProductService.cs"));
        var storefrontSearchService = File.ReadAllText(ApiPath("Services", "Storefront", "ProductSearchService.cs"));
        var storefrontHomeService = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontHomeService.cs"));
        var storefrontProductsController = File.ReadAllText(ApiPath("Controllers", "StorefrontProductsController.cs"));
        var cartService = File.ReadAllText(ApiPath("Services", "Cart", "CartService.cs"));
        var commerceHubQueryService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubQueryService.cs"));

        Assert.Contains("StorefrontEffectivePrice => ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(this)", productDocument, StringComparison.Ordinal);
        Assert.Contains("StorefrontVisiblePrice => ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(this)", productDocument, StringComparison.Ordinal);
        Assert.Contains("public static class ProductStorefrontVisibilityAuthority", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("ResolveSimpleProductPrice(ProductDocument product)", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("ResolveVisiblePrice(ProductDocument product)", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliatePublicPricing", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliateDiscoveryBasePrice", visibilityAuthority, StringComparison.Ordinal);
        Assert.Contains("var simplePrice = ResolveVisiblePrice(product)", visibilityAuthority, StringComparison.Ordinal);

        Assert.Contains("dbContext.ProductUnits", overlay, StringComparison.Ordinal);
        Assert.Contains("product.Units =", overlay, StringComparison.Ordinal);
        Assert.Contains("product.DefaultUnit =", overlay, StringComparison.Ordinal);
        Assert.Contains("product.PurchasePrice = sqlProduct.PurchasePrice", overlay, StringComparison.Ordinal);
        Assert.Contains("product.Features = DeserializeStringList(sqlProduct.FeaturesJson)", overlay, StringComparison.Ordinal);
        Assert.Contains("product.Faqs = DeserializeFaqList(sqlProduct.FaqJson)", overlay, StringComparison.Ordinal);
        Assert.Contains("product.Tags = DeserializeTags(sqlProduct.TagsJson, sqlProduct.Tags)", overlay, StringComparison.Ordinal);
        Assert.Contains("product.AiMetadata = ParseAiMetadata(sqlProduct.AiMetadataJson)", overlay, StringComparison.Ordinal);

        Assert.Contains("Price = (double)product.StorefrontVisiblePrice", searchMapper, StringComparison.Ordinal);

        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", storefrontProductService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", storefrontSearchService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", storefrontHomeService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(doc)", storefrontProductsController, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(product)", cartService, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.EvaluateVariant(product, variant)", cartService, StringComparison.Ordinal);
        Assert.Contains("?? product.StorefrontEffectivePrice", commerceHubQueryService, StringComparison.Ordinal);

        Assert.DoesNotContain("product.OnlinePrice.GetValueOrDefault() > 0", storefrontHomeService, StringComparison.Ordinal);
        Assert.DoesNotContain(": product.SalePrice", storefrontHomeService, StringComparison.Ordinal);
        Assert.DoesNotContain("variant.OnlinePrice ?? variant.Price", cartService, StringComparison.Ordinal);

        Assert.Contains("PromotionVisibilityProjectionSupport.BuildApplyProductPromotionUpdate", promotion, StringComparison.Ordinal);
        Assert.Contains("PromotionVisibilityProjectionSupport.BuildClearProductPromotionUpdate", promotion, StringComparison.Ordinal);
        Assert.DoesNotContain("_productCollection.ReplaceOneAsync(", promotion, StringComparison.Ordinal);
        Assert.Contains("StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert", writer, StringComparison.Ordinal);
        Assert.Contains("StorefrontProductProjectionMutationKind.ReviewedVisibilityTombstone", writer, StringComparison.Ordinal);
        Assert.Contains("_mutationBoundary.ApplyAsync", writer, StringComparison.Ordinal);
        Assert.Contains("_repository.UpsertAsync", mutationBoundary, StringComparison.Ordinal);
        Assert.Contains("_repository.TombstoneIfFreshAsync", mutationBoundary, StringComparison.Ordinal);

        var runtimeFiles = Directory
            .GetFiles(Path.Combine(Root, "PointOfSale.API"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "PointOfSale.Infrastructure"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith("ProductSqlAuthorityProjectionWriter.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("StorefrontProductProjectionMutationBoundary.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("ProductReadRepository.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("IProductReadRepository", StringComparison.Ordinal) &&
                       (source.Contains(".UpsertAsync(", StringComparison.Ordinal) ||
                        source.Contains(".TombstoneIfFreshAsync(", StringComparison.Ordinal));
            })
            .ToList();

        Assert.True(
            runtimeFiles.Count == 0,
            "Direct product projection repo writes must stay behind StorefrontProductProjectionMutationBoundary and ProductSqlAuthorityProjectionWriter. Offenders: " +
            string.Join(", ", runtimeFiles.Select(Path.GetFileName)));
    }

    [Fact]
    public void AffiliateAndPublicPricing_MustConvergeToStorefrontAuthority()
    {
        var productDocument = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductDocument.cs"));
        var affiliatePublic = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var affiliateIntelligence = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductIntelligenceProjectionConsumer.cs"));
        var affiliateDiscovery = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "AffiliateProductDiscoveryService.cs"));
        var mongoDbContext = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "MongoDbContext.cs"));
        var publicController = File.ReadAllText(ApiPath("Controllers", "PublicAffiliateProductsController.cs"));
        var affiliatePortal = File.ReadAllText(ApiPath("Controllers", "AffiliatePortalController.cs"));

        var visibilityAuthority = File.ReadAllText(InfrastructurePath("MongoDB", "Documents", "ProductStorefrontVisibilityAuthority.cs"));

        Assert.Contains("TryResolveAffiliatePublicPricing", productDocument, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliateDiscoveryBasePrice", productDocument, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.TryResolveAffiliatePublicPricing", productDocument, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.TryResolveAffiliateDiscoveryBasePrice", productDocument, StringComparison.Ordinal);
        Assert.Contains("var simplePrice = ResolveVisiblePrice(product)", visibilityAuthority, StringComparison.Ordinal);

        Assert.Contains("LoadAffiliatePolicyAuthorityAsync", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("!product.IsOnlineVisible", affiliatePublic, StringComparison.Ordinal);
        Assert.Contains("product.TryResolveAffiliatePublicPricing(out var pricing)", affiliatePublic, StringComparison.Ordinal);
        Assert.DoesNotContain("price = product.EffectivePrice", affiliatePublic, StringComparison.Ordinal);

        Assert.Contains("LoadCurrentStorefrontAuthorityProductAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("TryResolveAffiliateDiscoveryBasePrice(out var basePrice)", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("UpsertProductIntelligenceAsync", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("IsDuplicateMongoKeyException", affiliateIntelligence, StringComparison.Ordinal);
        Assert.Contains("new UpdateOptions { IsUpsert = true }", affiliateIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("var basePrice = evt.OnlinePrice.HasValue", affiliateIntelligence, StringComparison.Ordinal);

        Assert.Contains("idx_product_tenant_unique", mongoDbContext, StringComparison.Ordinal);

        Assert.Contains("p => p.InStock", affiliateDiscovery, StringComparison.Ordinal);

        Assert.Contains("Field(\"inStock\")", publicController, StringComparison.Ordinal);
        Assert.Contains("Field(p => p.MaxVariantPrice)", publicController, StringComparison.Ordinal);
        Assert.Contains("Field(p => p.MinVariantPrice)", publicController, StringComparison.Ordinal);

        Assert.Contains("Filter.Gt(d => d.BasePrice, 0m)", affiliatePortal, StringComparison.Ordinal);
        Assert.Contains("Filter.Eq(d => d.IsOutOfStock, false)", affiliatePortal, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionVisibilityConsumers_CollapseDuplicateSideEffectsAndRebuildFromSql()
    {
        var promotion = File.ReadAllText(InfrastructurePath("Consumers", "PromotionProjectionConsumer.cs"));
        var promotionSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductPromotionSearchIndexConsumer.cs"));
        var affiliate = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var commerceHub = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var repair = File.ReadAllText(Path.Combine(Root, "PointOfSale.API", "Services", "ReadModels", "DiscountReadModelRepairService.cs"));

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", promotion, StringComparison.Ordinal);
        Assert.Contains("await fence.CompleteAsync(", promotion, StringComparison.Ordinal);
        Assert.Contains("ResolvePromotionEventId(@event),", promotion, StringComparison.Ordinal);
        Assert.Contains("BuildPromotionDocument(sqlPromotion", promotion, StringComparison.Ordinal);
        Assert.Contains("RefreshAffectedPromotionVisibilityAsync", promotion, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", promotion, StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", promotionSearch, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", promotionSearch, StringComparison.Ordinal);
        Assert.Contains("LoadSqlPromotionAuthorityAsync", promotionSearch, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", promotionSearch, StringComparison.Ordinal);
        Assert.Contains("LoadWinningSqlProductPromotionAsync", promotionSearch, StringComparison.Ordinal);
        Assert.Contains("PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay", promotionSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", promotionSearch, StringComparison.Ordinal);

        Assert.Contains("PromotionConsumerName", affiliate, StringComparison.Ordinal);
        Assert.Contains("SyncPromotionAffectedProductWithFenceAsync", affiliate, StringComparison.Ordinal);
        Assert.DoesNotContain("completeFence: false", affiliate, StringComparison.Ordinal);
        Assert.Contains("completeFence: true", affiliate, StringComparison.Ordinal);
        Assert.Contains("fence.CompleteAsync", affiliate, StringComparison.Ordinal);
        Assert.Contains("PolicyChangedConsumerName", affiliate, StringComparison.Ordinal);
        Assert.Contains("ConsumeAffiliateProductPolicyChangedFromSqlAsync", affiliate, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliatePolicyAuthorityAsync", affiliate, StringComparison.Ordinal);
        Assert.Contains("RefreshPublicAffiliateStoreAsync", affiliate, StringComparison.Ordinal);

        Assert.Contains("PromotionConsumerName", commerceHub, StringComparison.Ordinal);
        Assert.Contains("RefreshPromotionAdjustedPricesWithFenceAsync", commerceHub, StringComparison.Ordinal);
        Assert.Contains("fence.CompleteAsync", commerceHub, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", repair, StringComparison.Ordinal);
        Assert.Contains("PromotionVisibilityProjectionSupport.SelectWinningProductPromotion", repair, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", repair, StringComparison.Ordinal);
        Assert.Contains("BuildApplyProductPromotionUpdate(product)", repair, StringComparison.Ordinal);
    }

    [Fact]
    public void HomepageRecommendationPaths_MustRecomposeDiscoveryVisibilityFromSql()
    {
        var storefrontHome = File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontHomeService.cs"));
        var homepageRead = File.ReadAllText(ApiPath("Services", "Homepage", "HomepageSectionReadService.cs"));
        var homepageAdmin = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminHomepageSectionManagementService.cs"));
        var marketplaceProjection = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var cacheInvalidation = File.ReadAllText(InfrastructurePath("Consumers", "ProductCacheInvalidationConsumer.cs"));
        var redisInvalidation = File.ReadAllText(ApiPath("Consumers", "RedisInvalidationConsumer.cs"));

        Assert.Contains("RecomposeProductCardsFromSqlAuthorityAsync", storefrontHome, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontHome, StringComparison.Ordinal);
        Assert.Contains("ProductStorefrontVisibilityAuthority.Evaluate(document)", storefrontHome, StringComparison.Ordinal);
        Assert.Contains("_dbContext.AffiliateProductPolicies", storefrontHome, StringComparison.Ordinal);
        Assert.DoesNotContain("product.OnlinePrice.GetValueOrDefault() > 0", storefrontHome, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductVariantLifecycleAuthority.IsPubliclyVisibleLineage", storefrontHome, StringComparison.Ordinal);

        Assert.Contains("SanitizeDiscoveryReferencesAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("LoadLiveCommerceHubListingIdsAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", homepageRead, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == Core.Enums.PublishStatus.Published", homepageRead, StringComparison.Ordinal);

        Assert.Contains("_dbContext.CommerceHubListings", homepageAdmin, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", homepageAdmin, StringComparison.Ordinal);
        Assert.Contains("product.IsOnlineVisible", homepageAdmin, StringComparison.Ordinal);
        Assert.Contains("product.PublishStatus == PublishStatus.Published", homepageAdmin, StringComparison.Ordinal);
        Assert.DoesNotContain("CountDocumentsAsync(productFilter", homepageAdmin, StringComparison.Ordinal);

        Assert.Contains("SanitizeSectionsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("LoadLiveListingIdsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("ProductVariantLifecycleAuthority.IsPubliclyVisibleLineage", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains(".WherePubliclyVisibleLineage()", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("ShouldSkipCuratedSection", marketplaceProjection, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.Homepage", marketplaceProjection, StringComparison.Ordinal);

        Assert.Contains("nameof(ProductStockChangedEvent)", cacheInvalidation, StringComparison.Ordinal);
        Assert.Contains("invalidateHomepage: true", cacheInvalidation, StringComparison.Ordinal);
        Assert.Contains("marketplace:homepage", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RemoveByPatternAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RunMarketplaceInvalidationWithFenceAsync", redisInvalidation, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchAndAffiliateRepairPaths_MustRecomposeCurrentAuthorityBeforeVisibilityMutation()
    {
        var productUpdatedSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductUpdatedSearchIndexConsumer.cs"));
        var productStockPriceSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockPriceSearchIndexConsumer.cs"));
        var productDeletedSearch = File.ReadAllText(InfrastructurePath("Consumers", "ProductDeletedSearchIndexConsumer.cs"));
        var affiliate = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateProductPolicyProjectionConsumer.cs"));
        var repair = File.ReadAllText(ApiPath("Services", "ReadModels", "DiscountReadModelRepairService.cs"));
        var adminSync = File.ReadAllText(ApiPath("Controllers", "AdminSyncController.cs"));
        var adminDataSync = File.ReadAllText(ApiPath("Controllers", "AdminDataSyncController.cs"));
        var adminSearchSync = File.ReadAllText(ApiPath("Controllers", "AdminSearchSyncController.cs"));
        var productsController = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));
        var categoryLineageAuthority = File.ReadAllText(ApiPath("Services", "Catalog", "CategoryLineageAuthority.cs"));
        var categoryRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "CategoryReadRepository.cs"));
        var affiliateCategoryConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", "AffiliateCategoryUpdatedConsumer.cs"));
        var affiliateReindexScript = File.ReadAllText(InfrastructurePath("Scripts", "AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs"));

        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productUpdatedSearch, StringComparison.Ordinal);
        Assert.Contains("if (!product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)", productUpdatedSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("if (@event.IsOnlineVisible.HasValue && !@event.IsOnlineVisible.Value)", productUpdatedSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext? dbContext = null", productStockPriceSearch, StringComparison.Ordinal);
        Assert.True(
            productStockPriceSearch.IndexOf("ProductSearchSqlAuthorityOverlay.ApplyAsync", StringComparison.Ordinal) <
            productStockPriceSearch.IndexOf("if (!product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)", StringComparison.Ordinal),
            "Product stock/price search sync must apply SQL authority before deleting hidden search documents.");
        Assert.Contains("IConsumer<ProductPublishStatusChangedEvent>", productDeletedSearch, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", productDeletedSearch, StringComparison.Ordinal);
        Assert.Contains("sqlProjection is { IsActive: true, IsOnlineVisible: true, IsSearchable: true }", productDeletedSearch, StringComparison.Ordinal);

        Assert.Contains("SyncProductSearchAffiliateVisibilityFromSqlAsync", affiliate, StringComparison.Ordinal);
        Assert.Contains("CountDocumentsAsync", affiliate, StringComparison.Ordinal);
        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", affiliate, StringComparison.Ordinal);

        Assert.Contains("BuildApplyProductPromotionUpdate", repair, StringComparison.Ordinal);

        Assert.Contains("IProductSqlAuthorityProjectionWriter", adminSync, StringComparison.Ordinal);
        Assert.Contains("ApplySqlProductRebuildAuthorityAsync", adminSync, StringComparison.Ordinal);
        Assert.Contains("_productProjectionWriter.ApplyAsync", adminSync, StringComparison.Ordinal);
        Assert.Contains("Admin product rebuild converged to tombstone from SQL authority", adminSync, StringComparison.Ordinal);

        Assert.Contains("new UpdateOneModel<CategoryDocument>", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("new UpdateOneModel<ShippingMethodDocument>", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoryDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleShippingMethodDocumentsAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("DeleteManyAsync", adminDataSync, StringComparison.Ordinal);
        Assert.Contains("lineageSnapshot.IsLiveInLineage(category.CategoryId)", adminDataSync, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.IsActive, isVisible)", adminDataSync, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.IsOnlineVisible, isVisible)", adminDataSync, StringComparison.Ordinal);
        Assert.Contains(".Set(d => d.IsDeleted, shippingMethod.IsDeleted)", adminDataSync, StringComparison.Ordinal);
        Assert.DoesNotContain("new ReplaceOneModel<CategoryDocument>", adminDataSync, StringComparison.Ordinal);
        Assert.DoesNotContain("new ReplaceOneModel<ShippingMethodDocument>", adminDataSync, StringComparison.Ordinal);

        Assert.Contains("DeleteProductFromSearchAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteCategoryFromSearchAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleCategoriesAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("ProductSearchDocumentMapper.MapFromProductDocument", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("!lineageSnapshot.IsLiveInLineage(row.CategoryId)", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("await _searchIndexService.DeleteProductAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.Contains("await _searchIndexService.DeleteCategoryAsync", adminSearchSync, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductSyncQuery(", adminSearchSync, StringComparison.Ordinal);

        Assert.Contains("CategoryLineageAuthority.RequireLiveCategoryAsync", productsController, StringComparison.Ordinal);
        Assert.Contains("CategoryId must reference a live category with a valid SQL parent lineage.", productsController, StringComparison.Ordinal);

        Assert.Contains("ParentCategoryId", categoryLineageAuthority, StringComparison.Ordinal);
        Assert.Contains("row.IsDeleted", categoryLineageAuthority, StringComparison.Ordinal);
        Assert.Contains("parentId == Guid.Empty", categoryLineageAuthority, StringComparison.Ordinal);
        Assert.Contains("visiting.Add(categoryId)", categoryLineageAuthority, StringComparison.Ordinal);

        Assert.Contains("PruneInvalidProjectionLineage", categoryRepository, StringComparison.Ordinal);
        Assert.Contains("IsProjectionLineageVisible", categoryRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("Orphan category treated as root", File.ReadAllText(ApiPath("Services", "Storefront", "StorefrontCategoryService.cs")), StringComparison.Ordinal);

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", affiliateCategoryConsumer, StringComparison.Ordinal);
        Assert.Contains("ApplyCurrentCategoryAuthorityAsync", affiliateCategoryConsumer, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", affiliateCategoryConsumer, StringComparison.Ordinal);
        Assert.Contains("NullifyCategoryProjectionAsync", affiliateCategoryConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain(".Set(d => d.AffiliateCategoryName, evt.NewName)", affiliateCategoryConsumer, StringComparison.Ordinal);

        Assert.Contains("PublicAffiliateProducts.UpdateOneAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("AffiliateProductIntelligence.UpdateOneAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("_elasticClient.UpdateAsync<PublicAffiliateProductSearchDocument, object>", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateVisibilityAuthorityAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("CurrencyNormalizationHelper.NormalizeCurrencyOrThrow", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("IsAffiliateVisibilityAllowed", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("RemoveStalePublicAffiliateProductAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("ProductIsVisible = !row.product.IsDeleted && row.product.IsActive && row.product.IsOnlineVisible", affiliateReindexScript, StringComparison.Ordinal);
        Assert.Contains("PolicyIsVisible = !policy.IsDeleted && policy.IsEnabled", affiliateReindexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"USD\"", affiliateReindexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicAffiliateProducts.ReplaceOneAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateProductIntelligence.ReplaceOneAsync", affiliateReindexScript, StringComparison.Ordinal);
        Assert.DoesNotContain("_elasticClient.IndexAsync(searchDoc", affiliateReindexScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAuthorityValidator_RejectsStaleAndCorruptedAuthority()
    {
        var now = DateTime.UtcNow;

        Assert.True(ProjectionAuthorityValidator.IsIncomingEventStale(now, 11, now.AddSeconds(-1), 10));
        Assert.False(ProjectionAuthorityValidator.IsIncomingEventStale(now, 10, now.AddSeconds(1), 11));

        var stale = ProjectionAuthorityValidator.ValidateSqlVersion(11, 10, requireExactVersion: true);
        var corrupted = ProjectionAuthorityValidator.ValidateSqlVersion(10, 11, requireExactVersion: true);

        Assert.False(stale.IsAccepted);
        Assert.Equal(ProjectionAuthorityValidationFailure.StaleReplay, stale.Failure);
        Assert.False(corrupted.IsAccepted);
        Assert.Equal(ProjectionAuthorityValidationFailure.FutureOrCorruptedVersion, corrupted.Failure);
    }

    [Fact]
    public void ProjectionTombstoneGuard_PreventsStaleResurrection()
    {
        var deletedAt = DateTime.UtcNow;

        Assert.True(ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent(
            deletedAt,
            tombstoneVersion: 11,
            deletedAt.AddSeconds(1),
            eventVersion: 10));

        Assert.True(ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent(
            deletedAt,
            tombstoneVersion: 10,
            deletedAt,
            eventVersion: 10));

        Assert.False(ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent(
            deletedAt,
            tombstoneVersion: 10,
            deletedAt.AddSeconds(1),
            eventVersion: 11));
    }
}
