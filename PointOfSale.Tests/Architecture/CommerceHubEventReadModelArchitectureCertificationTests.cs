namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubEventReadModelArchitectureCertificationTests
{
    [Fact]
    public void RuntimeEventReadModelPipeline_MustKeepSqlAsOnlyBusinessAuthority()
    {
        var outboxService = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxService.cs");
        var outboxProcessor = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs");
        var outboxMessage = ReadRepoFile("PointOfSale.Core/Entities/MarketplaceEventOutboxMessage.cs");
        var eventPublisher = ReadRepoFile("PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var consumerBase = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Marketplace/MarketplaceIdempotentConsumer.cs");
        var projectionProcessor = ReadRepoFile("PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs");
        var fence = ReadRepoFile("PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs");
        var projectionServices = ReadRepoFile("PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs");
        var productRepo = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceProductReadRepository.cs");
        var orderRepo = ReadRepoFile("PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceOrderReadRepository.cs");
        var cache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var marketplaceReads = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceReadController.cs");
        var searchOverlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var reindex = ReadRepoFile("PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs");
        var repair = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairService.cs");
        var repairSupport = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubOrderProjectionRepairSupport.cs");
        var checkoutIdentity = ReadRepoFile("PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts");
        var authBootstrap = ReadRepoFile("PointOfSale.Web/src/components/AuthBootstrap.jsx");

        Assert.Contains("_dbContext.Set<MarketplaceEventOutboxMessage>().Add(message)", outboxService, StringComparison.Ordinal);
        Assert.Contains("EnsureMarketplaceEventCurrencyAsync", outboxService, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", outboxService, StringComparison.Ordinal);

        Assert.Contains("HasUnpublishedEarlierEntityEventAsync", outboxProcessor, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", outboxProcessor, StringComparison.Ordinal);
        Assert.Contains("publishEndpoint.Publish", outboxProcessor, StringComparison.Ordinal);
        Assert.Contains("MarkPublished", outboxProcessor, StringComparison.Ordinal);
        Assert.Contains("@event.Version != @event.EventVersion", outboxMessage, StringComparison.Ordinal);

        Assert.Contains("UseBusOutbox", program, StringComparison.Ordinal);
        Assert.Contains("UsingRabbitMq", program, StringComparison.Ordinal);
        Assert.Contains("UseMessageRetry", program, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalPublishBoundary", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("PublishSensitiveAsync", eventPublisher, StringComparison.Ordinal);
        Assert.Contains("Authoritative integration events must be published before the source mutation is committed", eventPublisher, StringComparison.Ordinal);

        Assert.Contains("MarketplaceProjectionOutboxMessage.Create", consumerBase, StringComparison.Ordinal);
        Assert.Contains("ProcessedEvent", consumerBase, StringComparison.Ordinal);
        Assert.Contains("projection outbox duplicate", consumerBase, StringComparison.Ordinal);
        Assert.Contains("DispatchAsync", projectionProcessor, StringComparison.Ordinal);
        Assert.Contains("ProcessedEvent", projectionProcessor, StringComparison.Ordinal);
        Assert.Contains("TryAcquireAsync", fence, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync", fence, StringComparison.Ordinal);

        foreach (var marker in new[]
        {
            "SqlTruthRebuildGuard.RequireSqlAuthority",
            "CommerceHubListings",
            "ProductVariants",
            "ThenInclude(variant => variant.Stocks)",
            "ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync",
            "_dbContext.Orders",
            "FilterPublicReadAsync"
        })
        {
            Assert.Contains(marker, projectionServices, StringComparison.Ordinal);
        }

        Assert.Contains("LastEntityMutationVersion", productRepo, StringComparison.Ordinal);
        Assert.Contains("DeleteIfFreshAsync", productRepo, StringComparison.Ordinal);
        Assert.Contains("LastEntityMutationVersion", orderRepo, StringComparison.Ordinal);
        Assert.Contains("filterBuilder.Eq(x => x.OnlineCustomerId, onlineCustomerId.Value)", orderRepo, StringComparison.Ordinal);

        Assert.Contains("projection lag exceeded threshold", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KeyDeleteAsync", cache, StringComparison.Ordinal);
        Assert.Contains("CacheKeyBuilder.MarketplaceProductsListKey", marketplaceReads, StringComparison.Ordinal);
        Assert.Contains("FilterPublicReadAsync", marketplaceReads, StringComparison.Ordinal);

        Assert.Contains("SqlTruthRebuildGuard.RequireSqlAuthority", searchOverlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("DeleteStaleSearchDocumentsAsync", reindex, StringComparison.Ordinal);
        Assert.Contains("CountSqlVisibleCommerceHubListingsAsync", reindex, StringComparison.Ordinal);

        Assert.Contains("BuildProjectionDocument(order)", repair, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginProjection()", repair, StringComparison.Ordinal);
        Assert.Contains("ProjectionProvesSqlAuthority", repair, StringComparison.Ordinal);
        Assert.Contains("ProjectionChecksum", repairSupport, StringComparison.Ordinal);

        Assert.Contains("can never be restored from", checkoutIdentity, StringComparison.Ordinal);
        Assert.Contains("return null;", checkoutIdentity, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", authBootstrap, StringComparison.Ordinal);
        Assert.Contains("pageshow", authBootstrap, StringComparison.Ordinal);
        Assert.Contains("popstate", authBootstrap, StringComparison.Ordinal);
        Assert.Contains("online", authBootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationFreeze_MustRecordArchFourEventReadModelCertification()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-event-read-model-architecture-review-findings.md");

        foreach (var marker in new[]
        {
            "ARCH-4 CommerceHub Event & Read Model Architecture Review",
            "Outbox Findings",
            "RabbitMQ Findings",
            "Consumer Findings",
            "Mongo Findings",
            "Redis Findings",
            "Elasticsearch Findings",
            "Frontend Findings",
            "Projection Findings",
            "Cache Findings",
            "Replay Findings",
            "Recovery Findings",
            "Rebuild Findings",
            "Event Ordering Findings",
            "Eventual Consistency Findings",
            "Failure Findings",
            "Tenant Findings",
            "Cross-Domain Findings",
            "Architectural Risks",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
