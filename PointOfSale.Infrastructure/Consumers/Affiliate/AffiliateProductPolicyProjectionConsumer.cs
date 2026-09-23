using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using Nest;
using Elasticsearch.Net;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Tenancy;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-5: Projects AffiliateProductPolicyChangedEvent to MongoDB and Elasticsearch.
/// AF-10B: Also maintains public_affiliate_stores read model for store discovery.
/// AF-11: Maintains public_affiliate_products read model and elasticsearch index.
/// </summary>
public class AffiliateProductPolicyProjectionConsumer : 
    IConsumer<AffiliateProductPolicyChangedEvent>,
    IConsumer<ProductMediaChangedEvent>,
    IConsumer<ProductUpdatedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductArchivedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>,
    IConsumer<ProductPriceChangedEvent>,
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductVariantCreatedEvent>,
    IConsumer<ProductVariantUpdatedEvent>,
    IConsumer<ProductVariantDeletedEvent>,
    IConsumer<VariantPriceUpdatedEvent>,
    IConsumer<ProductVariantStockChangedEvent>,
    IConsumer<PromotionCreatedEvent>,
    IConsumer<PromotionUpdatedEvent>,
    IConsumer<PromotionDeletedEvent>,
    IConsumer<PromotionExpiredEvent>,
    IConsumer<StoreProfileUpdatedEvent>,
    IConsumer<TenantCurrencyChangedEvent>
{
    private const int StoreProfileIndexBatchSize = 8;

    private readonly MongoDbContext _mongoDbContext;
    private readonly IElasticClient _elasticClient;
    private readonly ElasticsearchSettings _elasticSettings;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AffiliateProductPolicyProjectionConsumer> _logger;
    private const string PolicyChangedConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":" + nameof(AffiliateProductPolicyChangedEvent);
    private const string ProductPriceConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":" + nameof(ProductPriceChangedEvent);
    private const string ProductMediaConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":" + nameof(ProductMediaChangedEvent);
    private const string ProductLifecycleConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":ProductLifecycle";
    private const string ProductVariantTriggerConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":ProductVariantSqlAuthorityTrigger";
    private const string PromotionConsumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":Promotion";

    public AffiliateProductPolicyProjectionConsumer(
        MongoDbContext mongoDbContext,
        IElasticClient elasticClient,
        IOptions<ElasticsearchSettings> elasticSettings,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<AffiliateProductPolicyProjectionConsumer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _elasticClient = elasticClient;
        _elasticSettings = elasticSettings.Value;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateProductPolicyChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "[Affiliate:Projection:PolicyChanged] ProductId: {ProductId} | IsEnabled: {IsEnabled} | TenantId: {TenantId}",
            evt.ProductId, evt.IsEnabled, evt.TenantId);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogError(
                "[Affiliate:Projection:PolicyChanged] REJECTED: Missing TenantId | ProductId: {ProductId}",
                evt.ProductId);
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {evt.ProductId} policy event is missing TenantId.");
        }

        var tenantId = evt.TenantId.Value;
        await ConsumeAffiliateProductPolicyChangedFromSqlAsync(evt, tenantId, ct);
        return;

#if false
        // STEP 1: Update MongoDB ProductDocument
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(d => d.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(d => d.ProductId, evt.ProductId));

            var update = Builders<ProductDocument>.Update
                .Set(d => d.IsAffiliateEnabled, evt.IsEnabled)
                .Set(d => d.AffiliateEnabledAtUtc, evt.IsEnabled ? DateTime.UtcNow : null);

            var mongoResult = await _mongoDbContext.Products.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MongoDB update FAILED | ProductId: {ProductId}", evt.ProductId);
            throw new InvalidOperationException(
                $"AFFILIATE_PRODUCT_DOCUMENT_PROJECTION_FAILED: Product {evt.ProductId} could not be updated in MongoDB.",
                ex);
        }

        // STEP 2: Partial update Elasticsearch document
        if (_elasticSettings.Enabled)
        {
            try
            {
                var documentId = $"{tenantId}_{evt.ProductId}";

                await _elasticClient.UpdateAsync<ProductSearchDocument, object>(
                    documentId,
                    u => u
                        .Index(_elasticSettings.ProductsIndexName)
                        .Doc(new
                        {
                            IsAffiliateEnabled = evt.IsEnabled,
                            AffiliateEnabledAtUtc = evt.IsEnabled ? DateTime.UtcNow : (DateTime?)null
                        })
                        .RetryOnConflict(3),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Elasticsearch update FAILED | ProductId: {ProductId}", evt.ProductId);
                throw new InvalidOperationException(
                    $"AFFILIATE_PRODUCT_SEARCH_PROJECTION_FAILED: Product {evt.ProductId} could not be updated in Elasticsearch.",
                    ex);
            }
        }

        // STEP 3: AF-10B — Update public_affiliate_stores read model
        try
        {
            var storeFilter = Builders<PublicAffiliateStoreDocument>.Filter.Eq(d => d.TenantId, tenantId);

            if (evt.IsEnabled)
            {
                var existingStore = await _mongoDbContext.PublicAffiliateStores.Find(storeFilter).FirstOrDefaultAsync(ct);

                if (existingStore != null)
                {
                    var incrementUpdate = Builders<PublicAffiliateStoreDocument>.Update
                        .Inc(d => d.AffiliateProductCount, 1)
                        .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);

                    await _mongoDbContext.PublicAffiliateStores.UpdateOneAsync(storeFilter, incrementUpdate, cancellationToken: ct);
                }
                else
                {
                    var tenant = await _dbContext.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Id, t.Name, t.Slug }).FirstOrDefaultAsync(ct);

                    if (tenant == null || string.IsNullOrWhiteSpace(tenant.Slug))
                    {
                        throw new InvalidOperationException(
                            $"AFFILIATE_PUBLIC_STORE_AUTHORITY_NOT_READY: Tenant {tenantId} is missing or has no slug.");
                    }

                    if (tenant != null && !string.IsNullOrWhiteSpace(tenant.Slug))
                    {
                        var newStore = new PublicAffiliateStoreDocument
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            StoreSlug = tenant.Slug,
                            StoreName = tenant.Name,
                            LogoUrl = null,
                            AffiliateProductCount = 1,
                            UpdatedAtUtc = DateTime.UtcNow
                        };

                        await _mongoDbContext.PublicAffiliateStores.InsertOneAsync(newStore, cancellationToken: ct);
                    }
                }
            }
            else
            {
                var decrementUpdate = Builders<PublicAffiliateStoreDocument>.Update
                    .Inc(d => d.AffiliateProductCount, -1)
                    .Set(d => d.UpdatedAtUtc, DateTime.UtcNow);

                await _mongoDbContext.PublicAffiliateStores.UpdateOneAsync(storeFilter, decrementUpdate, cancellationToken: ct);

                var store = await _mongoDbContext.PublicAffiliateStores.Find(storeFilter).FirstOrDefaultAsync(ct);

                if (store is { AffiliateProductCount: <= 0 })
                {
                    await _mongoDbContext.PublicAffiliateStores.DeleteOneAsync(storeFilter, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAILED to update public store | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_STORE_PROJECTION_FAILED: Tenant {tenantId} public affiliate store projection could not be updated.",
                ex);
        }

        // AF-11: Sync Public Affiliate Product
        await SyncPublicAffiliateProductAsync(tenantId, evt.ProductId, ct);
#endif
    }

    public async Task Consume(ConsumeContext<ProductMediaChangedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning(
                "[Affiliate:Projection:ProductMedia] Missing TenantId | ProductId: {ProductId}",
                evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            evt,
            ProductMediaConsumerName,
            typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
            evt.ProductId,
            context.CancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "[Affiliate:Projection:ProductMedia] Duplicate or active media replay collapsed before public affiliate side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                evt.EventId,
                evt.ProductId,
                tenantId);
            return;
        }

        if (!await IsCurrentSqlProductMediaEventAsync(
                tenantId,
                evt.ProductId,
                evt.EventVersion,
                context.CancellationToken))
        {
            _logger.LogWarning(
                "[Affiliate:Projection:ProductMedia] Rejected stale or corrupted media event by SQL authority | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                evt.EventId,
                evt.ProductId,
                tenantId,
                evt.EventVersion);

            await fence.CompleteAsync(
                evt,
                ProductMediaConsumerName,
                typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
                evt.ProductId,
                context.CancellationToken);
            return;
        }

        await SyncPublicAffiliateProductAsync(tenantId, evt.ProductId, context.CancellationToken);
        await RefreshPublicAffiliateStoreAsync(tenantId, context.CancellationToken);

        await fence.CompleteAsync(
            evt,
            ProductMediaConsumerName,
            typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
            evt.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} update event is missing TenantId.");
        }

        await SyncPublicAffiliateProductAsync(context.Message.TenantId.Value, context.Message.ProductId, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<ProductVariantCreatedEvent> context) =>
        ConsumeProductVariantTriggerFromSqlAuthorityAsync(
            context.Message,
            context.Message.TenantId,
            context.Message.ProductId,
            context.Message.VariantId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductVariantUpdatedEvent> context) =>
        ConsumeProductVariantTriggerFromSqlAuthorityAsync(
            context.Message,
            context.Message.TenantId,
            context.Message.ProductId,
            context.Message.VariantId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductVariantDeletedEvent> context) =>
        ConsumeProductVariantTriggerFromSqlAuthorityAsync(
            context.Message,
            context.Message.TenantId,
            context.Message.ProductId,
            context.Message.VariantId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<VariantPriceUpdatedEvent> context) =>
        ConsumeProductVariantTriggerFromSqlAuthorityAsync(
            context.Message,
            context.Message.TenantId,
            context.Message.ProductId,
            context.Message.VariantId,
            context.CancellationToken);

    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} delete event is missing TenantId.");
        }

        await SyncProductLifecycleVisibilityWithFenceAsync(
            context.Message,
            typeof(ProductDeletedEvent).FullName ?? nameof(ProductDeletedEvent),
            context.Message.TenantId.Value,
            context.Message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductArchivedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} archive event is missing TenantId.");
        }

        await SyncProductLifecycleVisibilityWithFenceAsync(
            context.Message,
            typeof(ProductArchivedEvent).FullName ?? nameof(ProductArchivedEvent),
            context.Message.TenantId.Value,
            context.Message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} publish-status event is missing TenantId.");
        }

        await SyncProductLifecycleVisibilityWithFenceAsync(
            context.Message,
            typeof(ProductPublishStatusChangedEvent).FullName ?? nameof(ProductPublishStatusChangedEvent),
            context.Message.TenantId.Value,
            context.Message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductPriceChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} price event is missing TenantId.");
        }

        await SyncPublicAffiliateProductWithFenceAsync(
            context.Message,
            ProductPriceConsumerName,
            typeof(ProductPriceChangedEvent).FullName ?? nameof(ProductPriceChangedEvent),
            context.Message.ProductId,
            context.Message.TenantId.Value,
            context.Message.ProductId,
            completeFence: true,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductStockChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} stock event is missing TenantId.");
        }

        await SyncPublicAffiliateProductAsync(context.Message.TenantId.Value, context.Message.ProductId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {context.Message.ProductId} variant stock event is missing TenantId.");
        }

        if (context.Message.VariantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_VARIANT_REQUIRED: Product {context.Message.ProductId} variant stock event has empty VariantId | TenantId: {context.Message.TenantId.Value} | ProductId: {context.Message.ProductId}");
        }

        ProjectionConsumerEventFence? fence = null;
        var consumerName = nameof(AffiliateProductPolicyProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent);
        var eventType = typeof(ProductVariantStockChangedEvent).FullName ?? nameof(ProductVariantStockChangedEvent);
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                context.Message,
                consumerName,
                eventType,
                context.Message.VariantId,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:VariantStockChanged] Duplicate or active replay collapsed before public affiliate side effects | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    context.Message.EventId,
                    context.Message.ProductId,
                    context.Message.VariantId,
                    context.Message.TenantId);
                return;
            }

            await EnsureProductVariantStockProjectionReadyAsync(
                context.Message.TenantId.Value,
                context.Message.ProductId,
                context.Message.VariantId,
                context.Message.StockUpdatedAtUtc,
                context.Message.EventVersion,
                context.CancellationToken);

            await SyncPublicAffiliateProductAsync(context.Message.TenantId.Value, context.Message.ProductId, context.CancellationToken);

            await fence.CompleteAsync(
                context.Message,
                consumerName,
                eventType,
                context.Message.VariantId,
                context.CancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    public async Task Consume(ConsumeContext<PromotionCreatedEvent> context)
    {
        await SyncPromotionAffectedProductWithFenceAsync(
            context.Message,
            typeof(PromotionCreatedEvent).FullName ?? nameof(PromotionCreatedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionUpdatedEvent> context)
    {
        await SyncPromotionAffectedProductWithFenceAsync(
            context.Message,
            typeof(PromotionUpdatedEvent).FullName ?? nameof(PromotionUpdatedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionDeletedEvent> context)
    {
        await SyncPromotionAffectedProductWithFenceAsync(
            context.Message,
            typeof(PromotionDeletedEvent).FullName ?? nameof(PromotionDeletedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionExpiredEvent> context)
    {
        await SyncPromotionAffectedProductWithFenceAsync(
            context.Message,
            typeof(PromotionExpiredEvent).FullName ?? nameof(PromotionExpiredEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<StoreProfileUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue) return;

        var tenantId = context.Message.TenantId.Value;
        var eventId = context.Message.EventId.ToString();
        var ct = context.CancellationToken;
        var tenantProfile = await LoadActiveAffiliateTenantProfileAsync(tenantId, ct);

        // ATOMIC RESERVATION: Claim the event before doing expensive work
        // This prevents race conditions under parallel redelivery
        if (!await TryClaimEventAsync(eventId, nameof(StoreProfileUpdatedEvent), tenantId, ct))
        {
            _logger.LogDebug(
                "[Affiliate:Projection:StoreProfileUpdated] Event already claimed/processed | EventId: {EventId} | TenantId: {TenantId}",
                eventId, tenantId);
            return;
        }

        try
        {
            if (tenantProfile == null)
            {
                await DeleteAffiliateTenantVisibilityAsync(tenantId, ct);
                await MarkEventAsCompletedAsync(eventId, ct);
                return;
            }

            var filter = BuildFreshPublicProductStoreProfileEventFilter(
                tenantId,
                eventId,
                context.Message.EventVersion,
                context.Message.OccurredAtUtc);
            var update = Builders<PublicAffiliateProductDocument>.Update
                .Set(d => d.StoreName, tenantProfile.Name)
                .Set(d => d.StoreSlug, tenantProfile.Slug)
                .Set(d => d.LastStoreProfileEventVersion, context.Message.EventVersion)
                .Set(d => d.LastStoreProfileEventOccurredAtUtc, context.Message.OccurredAtUtc)
                .Set(d => d.LastStoreProfileEventId, eventId);

            var updateResult = await _mongoDbContext.PublicAffiliateProducts.UpdateManyAsync(filter, update, cancellationToken: ct);
            if (updateResult.MatchedCount == 0)
            {
                await MarkEventAsCompletedAsync(eventId, ct);
                return;
            }

            if (_elasticSettings.Enabled)
            {
                var indexingFailures = new List<Guid>();
                
                using var cursor = await _mongoDbContext.PublicAffiliateProducts
                    .Find(d =>
                        d.TenantId == tenantId &&
                        d.LastStoreProfileEventVersion == context.Message.EventVersion &&
                        d.LastStoreProfileEventOccurredAtUtc == context.Message.OccurredAtUtc &&
                        d.LastStoreProfileEventId == eventId)
                    .ToCursorAsync(ct);

                while (await cursor.MoveNextAsync(ct))
                {
                    foreach (var chunk in cursor.Current.Chunk(StoreProfileIndexBatchSize))
                    {
                        var batch = chunk.ToList();
                        foreach (var product in batch)
                        {
                            product.StoreName = tenantProfile.Name;
                            product.StoreSlug = tenantProfile.Slug;
                        }

                        var results = await Task.WhenAll(batch.Select(product => IndexPublicAffiliateProductAsync(product, ct)));
                        
                        // Track which products failed to index
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (!results[i])
                            {
                                indexingFailures.Add(batch[i].ProductId);
                            }
                        }
                    }
                }

                // If any indexing operations failed, throw to trigger retry
                if (indexingFailures.Count > 0)
                {
                    _logger.LogError(
                        "[Affiliate:Projection:StoreProfileUpdated] Elasticsearch indexing failed for {FailureCount} products | EventId: {EventId} | TenantId: {TenantId} | FailedProducts: {FailedProducts}",
                        indexingFailures.Count, eventId, tenantId, string.Join(", ", indexingFailures));
                    
                    await MarkEventAsFailedAsync(eventId, ct);
                    throw new InvalidOperationException($"Elasticsearch indexing failed for {indexingFailures.Count} products. Event will be retried.");
                }
            }

            await MarkEventAsCompletedAsync(eventId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Projection:StoreProfileUpdated] Processing failed | EventId: {EventId} | TenantId: {TenantId}",
                eventId, tenantId);
            
            await MarkEventAsFailedAsync(eventId, ct);
            throw; // Re-throw to trigger MassTransit retry
        }
    }

    private static FilterDefinition<PublicAffiliateProductDocument> BuildFreshPublicProductStoreProfileEventFilter(
        Guid tenantId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc)
    {
        var filter = Builders<PublicAffiliateProductDocument>.Filter;
        return filter.And(
            filter.Eq(d => d.TenantId, tenantId),
            filter.Or(
                filter.Exists(d => d.LastStoreProfileEventVersion, false),
                filter.Eq(d => d.LastStoreProfileEventVersion, 0),
                filter.Lt(d => d.LastStoreProfileEventVersion, eventVersion),
                filter.And(
                    filter.Eq(d => d.LastStoreProfileEventVersion, eventVersion),
                    filter.Or(
                        filter.Exists(d => d.LastStoreProfileEventOccurredAtUtc, false),
                        filter.Eq(d => d.LastStoreProfileEventOccurredAtUtc, null),
                        filter.Lt(d => d.LastStoreProfileEventOccurredAtUtc, occurredAtUtc),
                        filter.And(
                            filter.Eq(d => d.LastStoreProfileEventOccurredAtUtc, occurredAtUtc),
                            filter.Or(
                                filter.Exists(d => d.LastStoreProfileEventId, false),
                                filter.Eq(d => d.LastStoreProfileEventId, null),
                                filter.Eq(d => d.LastStoreProfileEventId, eventId)))))));
    }

    public async Task Consume(ConsumeContext<TenantCurrencyChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            return;
        }

        var tenantId = context.Message.TenantId.Value;
        var eventId = context.Message.EventId.ToString();
        var ct = context.CancellationToken;
        var tenantProfile = await LoadActiveAffiliateTenantProfileAsync(tenantId, ct);

        // ATOMIC RESERVATION: Claim the event before doing expensive work
        // This prevents race conditions under parallel redelivery
        if (!await TryClaimEventAsync(eventId, nameof(TenantCurrencyChangedEvent), tenantId, ct))
        {
            _logger.LogDebug(
                "[Affiliate:Projection:TenantCurrencyChanged] Event already claimed/processed | EventId: {EventId} | TenantId: {TenantId}",
                eventId, tenantId);
            return;
        }

        try
        {
            if (tenantProfile == null)
            {
                await DeleteAffiliateTenantVisibilityAsync(tenantId, ct);
                await MarkEventAsCompletedAsync(eventId, ct);
                return;
            }

            var currency = NormalizeCurrency(
                tenantProfile.Currency,
                $"Consumer: AffiliateProductPolicyProjectionConsumer | Event: TenantCurrencyChanged | TenantId: {tenantId}");

            // Process the event
            var filter = Builders<PublicAffiliateProductDocument>.Filter.Eq(d => d.TenantId, tenantId);
            var update = Builders<PublicAffiliateProductDocument>.Update.Set(d => d.Currency, currency);

            var updateResult = await _mongoDbContext.PublicAffiliateProducts.UpdateManyAsync(filter, update, cancellationToken: ct);
            if (updateResult.MatchedCount == 0)
            {
                // No products to update, but still mark event as processed to prevent retries
                await MarkEventAsCompletedAsync(eventId, ct);
                return;
            }

            if (_elasticSettings.Enabled)
            {
                var indexingFailures = new List<Guid>();
                
                using var cursor = await _mongoDbContext.PublicAffiliateProducts
                    .Find(filter)
                    .ToCursorAsync(ct);

                while (await cursor.MoveNextAsync(ct))
                {
                    foreach (var chunk in cursor.Current.Chunk(StoreProfileIndexBatchSize))
                    {
                        var batch = chunk.ToList();
                        foreach (var product in batch)
                        {
                            product.Currency = currency;
                        }

                        var results = await Task.WhenAll(batch.Select(product => IndexPublicAffiliateProductAsync(product, ct)));
                        
                        // Track which products failed to index
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (!results[i])
                            {
                                indexingFailures.Add(batch[i].ProductId);
                            }
                        }
                    }
                }

                // If any indexing operations failed, throw to trigger retry
                if (indexingFailures.Count > 0)
                {
                    _logger.LogError(
                        "[Affiliate:Projection:TenantCurrencyChanged] Elasticsearch indexing failed for {FailureCount} products | EventId: {EventId} | TenantId: {TenantId} | FailedProducts: {FailedProducts}",
                        indexingFailures.Count, eventId, tenantId, string.Join(", ", indexingFailures));
                    
                    await MarkEventAsFailedAsync(eventId, ct);
                    throw new InvalidOperationException($"Elasticsearch indexing failed for {indexingFailures.Count} products. Event will be retried.");
                }
            }

            // Mark event as processed only after successful completion of ALL operations
            await MarkEventAsCompletedAsync(eventId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Projection:TenantCurrencyChanged] Processing failed | EventId: {EventId} | TenantId: {TenantId}",
                eventId, tenantId);
            
            // Mark as failed so it can be retried
            await MarkEventAsFailedAsync(eventId, ct);
            throw; // Re-throw to trigger MassTransit retry
        }
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken ct)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(ct);

        return NormalizeCurrency(
            tenantCurrency,
            $"Consumer: AffiliateProductPolicyProjectionConsumer, Method: ResolveTenantCurrencyAsync, TenantId: {tenantId}");
    }

    private async Task<AffiliateTenantProfileAuthority?> LoadActiveAffiliateTenantProfileAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId &&
                             tenant.Status == PointOfSale.Core.Enums.TenantStatus.Active &&
                             tenant.DeletedAtUtc == null)
            .Select(tenant => new AffiliateTenantProfileAuthority(
                tenant.Name,
                tenant.Slug ?? string.Empty,
                tenant.Currency))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Atomically claim an event for processing using FindOneAndUpdate.
    /// Returns true if this consumer acquired the lease, false if already claimed/processed.
    /// </summary>
    private async Task<bool> TryClaimEventAsync(string eventId, string eventType, Guid tenantId, CancellationToken ct)
    {
        try
        {
            var consumerName = nameof(AffiliateProductPolicyProjectionConsumer);
            var compositeId = $"{consumerName}:{eventId}";
            var owner = $"{Environment.MachineName}_{Guid.NewGuid():N}";
            
            // Atomic reservation filter:
            // - Eq(e => e.Status, null): Matches documents where Status field exists and is null
            // - Eq(e => e.Status, "Failed"): Allows retry of failed events
            // - IsUpsert = true: Creates a new document when none exists (handles non-existence)
            // This combination ensures only one consumer can claim an event for processing.
            var filter = Builders<ProcessedEventDocument>.Filter.And(
                Builders<ProcessedEventDocument>.Filter.Eq(e => e.Id, compositeId),
                Builders<ProcessedEventDocument>.Filter.Or(
                    Builders<ProcessedEventDocument>.Filter.Eq(e => e.Status, null),     // Status exists and is null
                    Builders<ProcessedEventDocument>.Filter.Eq(e => e.Status, "Failed")   // Allow retry of failed events
                )
            );

            var update = Builders<ProcessedEventDocument>.Update
                .Set(e => e.Status, "InProgress")
                .Set(e => e.Owner, owner)
                .Set(e => e.ClaimedAtUtc, DateTime.UtcNow)
                .Set(e => e.EventType, eventType)
                .Set(e => e.ConsumerName, consumerName)
                .Set(e => e.TenantId, tenantId)
                .SetOnInsert(e => e.Id, compositeId)
                .SetOnInsert(e => e.EventId, eventId);

            var options = new FindOneAndUpdateOptions<ProcessedEventDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.Before
            };

            var result = await _mongoDbContext.ProcessedEvents.FindOneAndUpdateAsync(filter, update, options, ct);

            // If result is null, we inserted a new document (acquired lease)
            // If result.Status is "Failed", we acquired the retry lease
            // Otherwise, event was already claimed/processed
            return result == null || result.Status == "Failed";
        }
        catch (MongoCommandException ex) when (ex.Code == 11000) // Duplicate key error
        {
            // Race condition: another consumer claimed it first
            _logger.LogDebug(
                "[Affiliate:Projection] Event claim race lost | EventId: {EventId}",
                eventId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Projection] Failed to claim event | EventId: {EventId}",
                eventId);
            // On error, don't claim - let MassTransit retry
            return false;
        }
    }

    /// <summary>
    /// Mark an event as successfully processed.
    /// </summary>
    private async Task MarkEventAsCompletedAsync(string eventId, CancellationToken ct)
    {
        try
        {
            var consumerName = nameof(AffiliateProductPolicyProjectionConsumer);
            var compositeId = $"{consumerName}:{eventId}";
            var filter = Builders<ProcessedEventDocument>.Filter.Eq(e => e.Id, compositeId);
            var update = Builders<ProcessedEventDocument>.Update
                .Set(e => e.Status, "Processed")
                .Set(e => e.ProcessedAtUtc, DateTime.UtcNow);

            await _mongoDbContext.ProcessedEvents.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Projection] Failed to mark event as completed | EventId: {EventId}",
                eventId);
            // Don't throw - the event was processed successfully
        }
    }

    /// <summary>
    /// Mark an event as failed so it can be retried.
    /// </summary>
    private async Task MarkEventAsFailedAsync(string eventId, CancellationToken ct)
    {
        try
        {
            var consumerName = nameof(AffiliateProductPolicyProjectionConsumer);
            var compositeId = $"{consumerName}:{eventId}";
            var filter = Builders<ProcessedEventDocument>.Filter.Eq(e => e.Id, compositeId);
            var update = Builders<ProcessedEventDocument>.Update
                .Set(e => e.Status, "Failed")
                .Set(e => e.ProcessedAtUtc, DateTime.UtcNow);

            await _mongoDbContext.ProcessedEvents.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Affiliate:Projection] Failed to mark event as failed | EventId: {EventId}",
                eventId);
            // Don't throw - we're already in error handling
        }
    }

    private async Task SyncPromotionAffectedProductWithFenceAsync(
        IntegrationEvent @event,
        string eventType,
        Guid? tenantId,
        string? scope,
        Guid? appliesToId,
        CancellationToken ct)
    {
        if (!tenantId.HasValue ||
            !string.Equals(scope, "product", StringComparison.OrdinalIgnoreCase) ||
            !appliesToId.HasValue)
        {
            return;
        }

        await SyncPublicAffiliateProductWithFenceAsync(
            @event,
            PromotionConsumerName,
            eventType,
            ResolvePromotionEventEntityId(@event),
            tenantId.Value,
            appliesToId.Value,
            completeFence: true,
            ct);
    }

    private async Task SyncPublicAffiliateProductWithFenceAsync(
        IntegrationEvent @event,
        string consumerName,
        string eventType,
        Guid entityId,
        Guid tenantId,
        Guid productId,
        bool completeFence,
        CancellationToken ct)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                consumerName,
                eventType,
                entityId,
                ct);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection] Duplicate or active pricing replay collapsed before public affiliate side effects | EventId: {EventId} | EventType: {EventType} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    eventType,
                    productId,
                    tenantId);
                return;
            }

            await SyncPublicAffiliateProductAsync(tenantId, productId, ct);

            if (completeFence)
            {
                await fence.CompleteAsync(
                    @event,
                    consumerName,
                    eventType,
                    entityId,
                    ct);
            }
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task ConsumeProductVariantTriggerFromSqlAuthorityAsync(
        IntegrationEvent @event,
        Guid? tenantId,
        Guid productId,
        Guid variantId,
        CancellationToken ct)
    {
        if (!tenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_TENANT_REQUIRED: Product {productId} variant event is missing TenantId.");
        }

        if (productId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_PRODUCT_REQUIRED: Variant {variantId} event has an empty ProductId | TenantId: {tenantId.Value}.");
        }

        if (variantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_PROJECTION_VARIANT_REQUIRED: Product {productId} event has an empty VariantId | TenantId: {tenantId.Value}.");
        }

        ProjectionConsumerEventFence? fence = null;
        var eventType = @event.GetType().FullName ?? @event.GetType().Name;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                ProductVariantTriggerConsumerName,
                eventType,
                variantId,
                ct);

            if (fence == null)
            {
                return;
            }

            await RecomposePublicAffiliateProductFromSqlAuthorityAsync(tenantId.Value, productId, ct);

            await fence.CompleteAsync(
                @event,
                ProductVariantTriggerConsumerName,
                eventType,
                variantId,
                ct);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task RecomposePublicAffiliateProductFromSqlAuthorityAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken ct)
    {
        await SyncPublicAffiliateProductAsync(tenantId, productId, ct);
        await RefreshPublicAffiliateStoreAsync(tenantId, ct);
    }

    private async Task SyncProductLifecycleVisibilityWithFenceAsync(
        IntegrationEvent @event,
        string eventType,
        Guid tenantId,
        Guid productId,
        CancellationToken ct)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                ProductLifecycleConsumerName,
                eventType,
                productId,
                ct);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:ProductLifecycle] Duplicate or active product lifecycle replay collapsed before public affiliate side effects | EventId: {EventId} | EventType: {EventType} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    eventType,
                    productId,
                    tenantId);
                return;
            }

            await SyncPublicAffiliateProductAsync(tenantId, productId, ct);
            await RefreshPublicAffiliateStoreAsync(tenantId, ct);

            await fence.CompleteAsync(
                @event,
                ProductLifecycleConsumerName,
                eventType,
                productId,
                ct);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task SyncPublicAffiliateProductAsync(Guid tenantId, Guid productId, CancellationToken ct)
    {
        try
        {
            var tenantLineage = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
                _dbContext,
                tenantId,
                productId,
                ct);

            if (!tenantLineage.IsCurrent)
            {
                _logger.LogWarning(
                    "[Affiliate:Projection] Suppressing public affiliate product because SQL tenant lineage is not current | TenantId: {TenantId} | ProductId: {ProductId} | Reason: {Reason} | CurrentTenantId: {CurrentTenantId}",
                    tenantId,
                    productId,
                    tenantLineage.Reason,
                    tenantLineage.CurrentTenantId);

                await DeletePublicAffiliateProductVisibilityAsync(tenantId, productId, ct);
                await RefreshPublicAffiliateStoreAsync(tenantId, ct);
                return;
            }

            var policyAuthority = await LoadAffiliatePolicyAuthorityAsync(tenantId, productId, ct);
            var productFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId));

            var product = await _mongoDbContext.Products.Find(productFilter).FirstOrDefaultAsync(ct)
                ?? new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId
                };

            product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                tenantId,
                ct,
                preserveProjectedStock: true);

            if (product == null ||
                !policyAuthority.IsEnabled ||
                !product.IsActive ||
                !product.IsOnlineVisible ||
                !product.IsSearchable)
            {
                await DeletePublicAffiliateProductVisibilityAsync(tenantId, productId, ct);
                return;
            }

            // AF-16E: Load tenant with StorefrontBaseUrl for multi-tenant promotion link correctness
            var tenant = await _dbContext.Tenants.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(t => t.Id == tenantId && t.IsActive && t.DeletedAtUtc == null)
                .Select(t => new { t.Name, t.Slug, t.StorefrontBaseUrl, t.Currency })
                .FirstOrDefaultAsync(ct);

            if (tenant == null)
            {
                await DeletePublicAffiliateProductVisibilityAsync(tenantId, productId, ct);
                await RefreshPublicAffiliateStoreAsync(tenantId, ct);
                return;
            }
            
            string storefrontBaseUrl;
            try
            {
                storefrontBaseUrl = StorefrontUrlAuthority.ResolveOrigin(
                    tenant.StorefrontBaseUrl,
                    tenant.Slug,
                    _configuration["Storefront:UrlTemplate"]);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PUBLIC_PRODUCT_URL_AUTHORITY_NOT_READY: Storefront URL cannot be resolved for tenant {tenantId}.",
                    ex);
            }

            // ═══════════════════════════════════════════════════════════════════════════════
            // AF-17A: STOREFRONT TRUTH ALIGNMENT
            // Use EXACT same logic as StorefrontProductService.MapToDto() and MapToListItemDto()
            // ═══════════════════════════════════════════════════════════════════════════════

            if (!product.TryResolveAffiliatePublicPricing(out var pricing) ||
                !pricing.InStock)
            {
                _logger.LogWarning(
                    "[AF-17A] Skipping non-purchasable affiliate/public product after storefront-authority recomposition | ProductId: {ProductId} | TenantId: {TenantId} | HasVariants: {HasVariants}",
                    productId,
                    tenantId,
                    product.HasVariants);

                await DeletePublicAffiliateProductVisibilityAsync(tenantId, productId, ct);
                return;
            }

            var doc = new PublicAffiliateProductDocument
            {
                ProductId = product.ProductId,
                ProductSlug = product.Slug ?? string.Empty,
                ProductName = product.Name,
                Description = product.Description ?? string.Empty,
                ThumbnailUrl = ResolvePrimaryProductImage(product) ?? string.Empty,
                
                // AF-17A: Pricing fields (mutually exclusive based on HasVariants)
                Price = pricing.Price,
                MinVariantPrice = pricing.MinVariantPrice,
                MaxVariantPrice = pricing.MaxVariantPrice,
                
                Currency = NormalizeCurrency(
                    tenant.Currency, 
                    $"Consumer: AffiliateProductPolicyProjectionConsumer | Method: SyncPublicAffiliateProductAsync | TenantId: {tenantId} | ProductId: {productId}"),
                StoreName = tenant.Name,
                StoreSlug = tenant.Slug ?? string.Empty,
                CategoryName = product.CategoryName ?? string.Empty,
                
                // AF-17A: Inventory fields (computed using Storefront logic)
                HasVariants = product.HasVariants,
                TotalAvailableStock = pricing.TotalAvailableStock,
                InStock = pricing.InStock,
                
                IsActive = product.IsActive,
                IsAffiliateEnabled = policyAuthority.IsEnabled,
                AffiliateEnabledAtUtc = product.AffiliateEnabledAtUtc ?? DateTime.UtcNow,
                TenantId = tenantId,
                StorefrontBaseUrl = storefrontBaseUrl
            };

            await _mongoDbContext.PublicAffiliateProducts.ReplaceOneAsync(
                BuildPublicAffiliateProductFilter(tenantId, productId),
                doc,
                new ReplaceOptions { IsUpsert = true },
                ct);

            var indexingSuccess = await IndexPublicAffiliateProductAsync(doc, ct);
            
            if (!indexingSuccess)
            {
                _logger.LogError(
                    "[AF-17A] Elasticsearch indexing failed | ProductId: {ProductId} | TenantId: {TenantId}",
                    productId, tenantId);
                throw new InvalidOperationException(
                    $"AFFILIATE_PUBLIC_PRODUCT_SEARCH_PROJECTION_FAILED: Product {productId} could not be indexed in Elasticsearch.");
            }
            else
            {
                _logger.LogInformation(
                    "[AF-17A] Product indexed | ProductId: {ProductId} | HasVariants: {HasVariants} | Stock: {Stock} | Price: {Price}",
                    productId,
                    doc.HasVariants,
                    doc.TotalAvailableStock,
                    doc.Price ?? doc.MinVariantPrice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AF-17A] Public sync failed | ProductId: {ProductId}", productId);
            throw;
        }
    }

    private async Task DeletePublicAffiliateProductVisibilityAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken ct)
    {
        await _mongoDbContext.PublicAffiliateProducts.DeleteOneAsync(
            BuildPublicAffiliateProductFilter(tenantId, productId),
            ct);

        await DeletePublicAffiliateSearchVisibilityAsync(tenantId, productId, ct);
    }

    private async Task DeleteAffiliateTenantVisibilityAsync(Guid tenantId, CancellationToken ct)
    {
        var filter = Builders<PublicAffiliateProductDocument>.Filter.Eq(document => document.TenantId, tenantId);
        var products = await _mongoDbContext.PublicAffiliateProducts
            .Find(filter)
            .Project(document => document.ProductId)
            .ToListAsync(ct);

        if (products.Count > 0)
        {
            await _mongoDbContext.PublicAffiliateProducts.DeleteManyAsync(filter, ct);
        }

        await _mongoDbContext.PublicAffiliateStores.DeleteOneAsync(
            Builders<PublicAffiliateStoreDocument>.Filter.Eq(document => document.TenantId, tenantId),
            ct);

        foreach (var productId in products)
        {
            await DeletePublicAffiliateSearchVisibilityAsync(tenantId, productId, ct);
        }
    }

    private async Task DeletePublicAffiliateSearchVisibilityAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken ct)
    {
        if (!_elasticSettings.Enabled)
        {
            return;
        }

        var scopedId = BuildPublicAffiliateSearchDocumentId(tenantId, productId);
        var scopedResponse = await _elasticClient.DeleteAsync<PublicAffiliateProductSearchDocument>(
            scopedId,
            descriptor => descriptor.Index(_elasticSettings.PublicAffiliateProductsIndexName),
            ct);

        if (!scopedResponse.IsValid && scopedResponse.Result != Result.NotFound)
        {
            _logger.LogWarning(
                "Failed to delete tenant-scoped public affiliate search document | TenantId: {TenantId} | ProductId: {ProductId} | Error: {Error}",
                tenantId,
                productId,
                scopedResponse.ServerError?.Error?.Reason ?? scopedResponse.OriginalException?.Message ?? "Unknown");
        }

        var legacyResponse = await _elasticClient.DeleteAsync<PublicAffiliateProductSearchDocument>(
            productId,
            descriptor => descriptor.Index(_elasticSettings.PublicAffiliateProductsIndexName),
            ct);

        if (!legacyResponse.IsValid && legacyResponse.Result != Result.NotFound)
        {
            _logger.LogWarning(
                "Failed to delete legacy product-id public affiliate search document | TenantId: {TenantId} | ProductId: {ProductId} | Error: {Error}",
                tenantId,
                productId,
                legacyResponse.ServerError?.Error?.Reason ?? legacyResponse.OriginalException?.Message ?? "Unknown");
        }
    }

    private static FilterDefinition<PublicAffiliateProductDocument> BuildPublicAffiliateProductFilter(
        Guid tenantId,
        Guid productId)
    {
        return Builders<PublicAffiliateProductDocument>.Filter.And(
            Builders<PublicAffiliateProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
            Builders<PublicAffiliateProductDocument>.Filter.Eq(document => document.ProductId, productId));
    }

    private static string BuildPublicAffiliateSearchDocumentId(Guid tenantId, Guid productId) =>
        TenantScopedDerivativeKey.ForTenantEntity(tenantId, productId);

    private async Task EnsureProductVariantStockProjectionReadyAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        DateTime stockUpdatedAtUtc,
        int eventVersion,
        CancellationToken ct)
    {
        stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(stockUpdatedAtUtc);

        var product = await _mongoDbContext.Products
            .Find(product =>
                product.TenantId == tenantId &&
                product.ProductId == productId)
            .FirstOrDefaultAsync(ct);

        if (product?.Variants == null || product.Variants.Count == 0)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_STOCK_PROJECTION_NOT_READY: Product {productId} has no variants in projection for variant {variantId} stock event version {eventVersion}.");
        }

        var variant = product.Variants.FirstOrDefault(item => item.VariantId == variantId);
        var variantStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(
            variant?.LastStockUpdatedAtUtc ?? variant?.StockUpdatedAtUtc);
        var variantStockEventVersion = variant?.LastStockEventVersion > 0
            ? variant.LastStockEventVersion
            : 0;

        if (variantStockUpdatedAtUtc == null ||
            variantStockUpdatedAtUtc.Value < stockUpdatedAtUtc ||
            (variantStockUpdatedAtUtc.Value == stockUpdatedAtUtc &&
             variantStockEventVersion < eventVersion))
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_PRODUCT_STOCK_PROJECTION_NOT_READY: Product {productId} variant {variantId} has not applied authoritative stock event version {eventVersion}.");
        }
    }

    private async Task ConsumeAffiliateProductPolicyChangedFromSqlAsync(
        AffiliateProductPolicyChangedEvent evt,
        Guid tenantId,
        CancellationToken ct)
    {
        ProjectionConsumerEventFence? fence = null;
        var eventType = typeof(AffiliateProductPolicyChangedEvent).FullName ?? nameof(AffiliateProductPolicyChangedEvent);

        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                evt,
                PolicyChangedConsumerName,
                eventType,
                evt.ProductId,
                ct);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[Affiliate:Projection:PolicyChanged] Duplicate or active replay collapsed before public affiliate side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    evt.EventId,
                    evt.ProductId,
                    tenantId);
                return;
            }

            var policyAuthority = await LoadAffiliatePolicyAuthorityAsync(tenantId, evt.ProductId, ct);

            await UpdateProductAffiliateVisibilityFromSqlAsync(
                tenantId,
                evt.ProductId,
                policyAuthority.IsEnabled,
                ct);

            await SyncProductSearchAffiliateVisibilityFromSqlAsync(
                tenantId,
                evt.ProductId,
                policyAuthority.IsEnabled,
                ct);

            await SyncPublicAffiliateProductAsync(tenantId, evt.ProductId, ct);
            await RefreshPublicAffiliateStoreAsync(tenantId, ct);

            await fence.CompleteAsync(
                evt,
                PolicyChangedConsumerName,
                eventType,
                evt.ProductId,
                ct);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task<AffiliatePolicyAuthoritySnapshot> LoadAffiliatePolicyAuthorityAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken ct)
    {
        var policy = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.ProductId == productId)
            .Select(item => new
            {
                item.IsEnabled,
                item.AffiliateCategoryId
            })
            .FirstOrDefaultAsync(ct);

        if (policy?.IsEnabled != true || !policy.AffiliateCategoryId.HasValue)
        {
            return new AffiliatePolicyAuthoritySnapshot(false);
        }

        var categorySelectable = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(category =>
                category.Id == policy.AffiliateCategoryId.Value &&
                category.IsActive &&
                !category.IsDeleted &&
                ((category.Level == 1 && category.ParentId == null) ||
                 (category.Level == 2 &&
                  category.ParentId.HasValue &&
                  _dbContext.AffiliateCategories
                      .IgnoreQueryFilters()
                      .Any(parent =>
                          parent.Id == category.ParentId.Value &&
                          parent.Level == 1 &&
                          parent.ParentId == null &&
                          parent.IsActive &&
                          !parent.IsDeleted))),
                ct);

        return new AffiliatePolicyAuthoritySnapshot(categorySelectable);
    }

    private async Task UpdateProductAffiliateVisibilityFromSqlAsync(
        Guid tenantId,
        Guid productId,
        bool isAffiliateEnabled,
        CancellationToken ct)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(document => document.ProductId, productId));

            var update = Builders<ProductDocument>.Update
                .Set(document => document.IsAffiliateEnabled, isAffiliateEnabled)
                .Set(document => document.AffiliateEnabledAtUtc, isAffiliateEnabled ? DateTime.UtcNow : null);

            var result = await _mongoDbContext.Products.UpdateOneAsync(
                filter,
                update,
                cancellationToken: ct);

            if (result.MatchedCount == 0 && isAffiliateEnabled)
            {
                throw new InvalidOperationException(
                    $"AFFILIATE_PRODUCT_DOCUMENT_PROJECTION_NOT_READY: Product {productId} is affiliate-enabled in SQL but Mongo product projection is missing.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MongoDB affiliate visibility sync failed | ProductId: {ProductId}", productId);
            throw new InvalidOperationException(
                $"AFFILIATE_PRODUCT_DOCUMENT_PROJECTION_FAILED: Product {productId} could not be updated in MongoDB from SQL affiliate policy authority.",
                ex);
        }
    }

    private async Task SyncProductSearchAffiliateVisibilityFromSqlAsync(
        Guid tenantId,
        Guid productId,
        bool isAffiliateEnabled,
        CancellationToken ct)
    {
        if (!_elasticSettings.Enabled)
        {
            return;
        }

        try
        {
            var productFilter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(product => product.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(product => product.ProductId, productId));

            var product = await _mongoDbContext.Products.Find(productFilter).FirstOrDefaultAsync(ct);
            if (product != null)
            {
                product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    product,
                    tenantId,
                    ct,
                    preserveProjectedStock: true);
            }

            if (product == null)
            {
                if (isAffiliateEnabled)
                {
                    throw new InvalidOperationException(
                        $"AFFILIATE_PRODUCT_SEARCH_PROJECTION_NOT_READY: Product {productId} is affiliate-enabled in SQL but product search projection base document is missing.");
                }

                var fence = await ProductSearchSqlAuthorityOverlay.LoadFenceAsync(
                    _dbContext,
                    tenantId,
                    productId,
                    ct) ?? throw new InvalidOperationException(
                        $"AFFILIATE_PRODUCT_SEARCH_TOMBSTONE_AUTHORITY_MISSING: Product {productId} has no durable SQL generation.");

                await IndexVersionedProductSearchDocumentAsync(new ProductSearchDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    ProductGeneration = fence.ProductGeneration,
                    IsTombstone = true,
                    IsDeleted = fence.IsDeleted,
                    IsActive = false,
                    IsOnlineVisible = false,
                    IsSearchable = false,
                    Name = string.Empty,
                    IndexedAtUtc = DateTime.UtcNow
                }, ct);

                return;
            }

            var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(product);
            await IndexVersionedProductSearchDocumentAsync(searchDocument, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch affiliate visibility sync failed | ProductId: {ProductId}", productId);
            throw new InvalidOperationException(
                $"AFFILIATE_PRODUCT_SEARCH_PROJECTION_FAILED: Product {productId} could not be synchronized to Elasticsearch from SQL affiliate policy authority.",
                ex);
        }
    }

    private async Task IndexVersionedProductSearchDocumentAsync(
        ProductSearchDocument document,
        CancellationToken cancellationToken)
    {
        var response = await _elasticClient.IndexAsync(
            document,
            descriptor => descriptor
                .Index(_elasticSettings.ProductsIndexName)
                .Id(document.GetDocumentId())
                .Version(ProductSearchIndexService.ToElasticsearchVersion(document.ProductGeneration))
                .VersionType(VersionType.External),
            cancellationToken);

        if (!response.IsValid &&
            response.ApiCall?.HttpStatusCode != 409 &&
            response.ServerError?.Status != 409)
        {
            throw new InvalidOperationException(
                response.ServerError?.Error?.Reason ??
                response.OriginalException?.Message ??
                $"Elasticsearch indexing failed for product {document.ProductId}.");
        }
    }

    private async Task<bool> IsCurrentSqlProductMediaEventAsync(
        Guid tenantId,
        Guid productId,
        int eventVersion,
        CancellationToken ct)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Id == productId)
            .Select(item => new
            {
                item.IsDeleted,
                item.IsActive,
                item.IsOnlineVisible,
                item.ProjectionMutationVersion
            })
            .SingleOrDefaultAsync(ct);

        if (product == null || product.IsDeleted || !product.IsActive || !product.IsOnlineVisible)
        {
            return false;
        }

        var sqlVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        return ProjectionAuthorityValidator.ValidateSqlVersion(
            sqlVersion,
            eventVersion,
            requireExactVersion: true).IsAccepted;
    }

    private static string? ResolvePrimaryProductImage(ProductDocument product)
    {
        var mediaImage = (product.Media ?? new List<ProductMediaDocument>())
            .Where(item => string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Position)
            .Select(item => item.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

        if (!string.IsNullOrWhiteSpace(mediaImage))
        {
            return mediaImage;
        }

        if (string.IsNullOrWhiteSpace(product.Images))
        {
            return null;
        }

        return product.Images
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private async Task RefreshPublicAffiliateStoreAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var visibleProductCount = (int)await _mongoDbContext.PublicAffiliateProducts
                .CountDocumentsAsync(
                    Builders<PublicAffiliateProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
                    cancellationToken: ct);

            var storeFilter = Builders<PublicAffiliateStoreDocument>.Filter.Eq(document => document.TenantId, tenantId);
            if (visibleProductCount <= 0)
            {
                await _mongoDbContext.PublicAffiliateStores.DeleteOneAsync(storeFilter, ct);
                return;
            }

            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item => item.Id == tenantId && item.IsActive && item.DeletedAtUtc == null)
                .Select(item => new
                {
                    item.Name,
                    item.Slug
                })
                .FirstOrDefaultAsync(ct);

            if (tenant == null || string.IsNullOrWhiteSpace(tenant.Slug))
            {
                await _mongoDbContext.PublicAffiliateStores.DeleteOneAsync(storeFilter, ct);
                return;
            }

            var storeDocument = new PublicAffiliateStoreDocument
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                StoreSlug = tenant.Slug,
                StoreName = tenant.Name,
                LogoUrl = null,
                AffiliateProductCount = visibleProductCount,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await _mongoDbContext.PublicAffiliateStores.ReplaceOneAsync(
                storeFilter,
                storeDocument,
                new ReplaceOptions { IsUpsert = true },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Public affiliate store refresh failed | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException(
                $"AFFILIATE_PUBLIC_STORE_PROJECTION_FAILED: Tenant {tenantId} public affiliate store projection could not be recomposed safely.",
                ex);
        }
    }

    private async Task<bool> IndexPublicAffiliateProductAsync(PublicAffiliateProductDocument doc, CancellationToken ct)
    {
        if (!_elasticSettings.Enabled) return true;

        var searchDoc = new PublicAffiliateProductSearchDocument
        {
            ProductId = doc.ProductId,
            ProductName = doc.ProductName,
            Description = doc.Description,
            CategoryName = doc.CategoryName,
            StoreName = doc.StoreName,
            StoreSlug = doc.StoreSlug,
            ProductSlug = doc.ProductSlug,
            ThumbnailUrl = doc.ThumbnailUrl,
            
            // AF-17A: Pricing fields (nullable, mutually exclusive)
            Price = doc.Price.HasValue ? (double?)doc.Price.Value : null,
            MinVariantPrice = doc.MinVariantPrice.HasValue ? (double?)doc.MinVariantPrice.Value : null,
            MaxVariantPrice = doc.MaxVariantPrice.HasValue ? (double?)doc.MaxVariantPrice.Value : null,
            
            Currency = doc.Currency,
            
            // AF-17A: Inventory fields
            HasVariants = doc.HasVariants,
            TotalAvailableStock = doc.TotalAvailableStock,
            InStock = doc.InStock,
            
            AffiliateEnabledAtUtc = doc.AffiliateEnabledAtUtc,
            IsAffiliateEnabled = doc.IsAffiliateEnabled,
            IsActive = doc.IsActive,
            StorefrontBaseUrl = doc.StorefrontBaseUrl,
            TenantId = doc.TenantId
        };

        await DeletePublicAffiliateSearchVisibilityAsync(doc.TenantId, doc.ProductId, ct);

        var response = await _elasticClient.IndexAsync(searchDoc, i => i
            .Index(_elasticSettings.PublicAffiliateProductsIndexName)
            .Id(BuildPublicAffiliateSearchDocumentId(doc.TenantId, doc.ProductId)), ct);

        if (!response.IsValid)
        {
            _logger.LogWarning("Failed to index public affiliate product | ProductId: {ProductId} | Error: {Error}", doc.ProductId, response.ServerError?.Error?.Reason ?? "Unknown");
            return false;
        }

        return true;
    }

    private static string NormalizeCurrency(string? currency, string context) =>
        CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
            currency,
            context);

    private static Guid ResolvePromotionEventEntityId(IntegrationEvent @event) =>
        @event switch
        {
            PromotionCreatedEvent created => created.PromotionId,
            PromotionUpdatedEvent updated => updated.PromotionId,
            PromotionDeletedEvent deleted => deleted.PromotionId,
            PromotionExpiredEvent expired => expired.PromotionId,
            _ => @event.EventId
        };

    private sealed record AffiliatePolicyAuthoritySnapshot(bool IsEnabled);

    private sealed record AffiliateTenantProfileAuthority(
        string Name,
        string Slug,
        string? Currency);
}
