using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-22: Projection consumer for Affiliate Product Intelligence.
///
/// Creates and maintains a rich read model for affiliate product discovery with:
/// - Full product details
/// - Inventory availability
/// - Variant-level stock
/// - Store context
/// - Commission intelligence
///
/// HARD CONSTRAINTS:
/// - NO changes to existing Affiliate projections
/// - SQL is re-read for stock authority before inventory visibility changes
/// - Mongo/product events are triggers and display metadata only
/// - Event-driven propagation remains derivative
/// - Tenant-safe
/// - Backward compatible
///
/// EVENTS CONSUMED:
/// - ProductCreatedEvent
/// - ProductUpdatedEvent
/// - ProductStockChangedEvent
/// - ProductVariantStockChangedEvent (AF-22 FIX)
/// - AffiliateProductPolicyChangedEvent
/// - StoreProfileUpdatedEvent
///
/// IDEMPOTENCY: ProcessedEventIds tracks which EventIds have been applied.
/// </summary>
public class AffiliateProductIntelligenceProjectionConsumer :
    IConsumer<ProductCreatedEvent>,
    IConsumer<ProductUpdatedEvent>,
    IConsumer<ProductMediaChangedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductArchivedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>,
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductVariantStockChangedEvent>, // AF-22 FIX
    IConsumer<AffiliateProductPolicyChangedEvent>,
    IConsumer<StoreProfileUpdatedEvent>,
    IConsumer<TenantCurrencyChangedEvent>
{
    private const int StoreProfileUpdateBatchSize = 250;
    private const string ProductMediaConsumerName = nameof(AffiliateProductIntelligenceProjectionConsumer) + ":" + nameof(ProductMediaChangedEvent);

    private readonly MongoDbContext _mongoDbContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AffiliateProductIntelligenceProjectionConsumer> _logger;
    private readonly int _lowStockThreshold;

    public AffiliateProductIntelligenceProjectionConsumer(
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<AffiliateProductIntelligenceProjectionConsumer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        
        // AF-22 HARDENING: Configurable low stock threshold
        _lowStockThreshold = configuration.GetValue<int>("AffiliateProductIntelligence:LowStockThreshold", 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT CREATED
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:ProductCreated] ProductId: {ProductId} | TenantId: {TenantId}",
            evt.ProductId, evt.TenantId);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductCreated] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;

        if (!await IsProductVisibleInSqlAsync(tenantId, evt.ProductId, ct))
        {
            await DeleteProductIntelligenceIfFreshAsync(
                tenantId,
                evt.ProductId,
                eventId,
                evt.EventVersion,
                evt.OccurredAtUtc,
                ct);
            return;
        }

        // Check idempotency
        var existingDoc = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.ProductId == evt.ProductId && d.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug("[AF-22:ProductCreated] Event already processed | EventId: {EventId}", eventId);
            return;
        }

        var authoritativeProduct = await LoadCurrentStorefrontAuthorityProductAsync(
            tenantId,
            evt.ProductId,
            evt.Name,
            evt.Description,
            evt.Images,
            evt.OccurredAtUtc,
            ct);
        if (authoritativeProduct == null ||
            !authoritativeProduct.TryResolveAffiliateDiscoveryBasePrice(out var basePrice))
        {
            await DeleteProductIntelligenceIfFreshAsync(
                tenantId,
                evt.ProductId,
                eventId,
                evt.EventVersion,
                evt.OccurredAtUtc,
                ct);
            return;
        }

        var productSlug = authoritativeProduct.Slug ?? evt.Name.ToLowerInvariant().Replace(" ", "-");

        // Get store context
        var storeContext = await GetStoreContextAsync(tenantId, productSlug, ct);

        // AF-22 HARDENING: Fail-fast guard for missing StorefrontUrl
        if (string.IsNullOrWhiteSpace(storeContext.StorefrontUrl))
        {
            _logger.LogError(
                "[AF-22:ProductCreated] Missing StorefrontBaseUrl | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, evt.ProductId);
            return; // Do NOT upsert broken document
        }

        // Get affiliate policy (commission rate)
        var policy = await _dbContext.AffiliateProductPolicies
            .Where(p => p.ProductId == evt.ProductId && p.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        var commissionRate = policy?.CommissionRate ?? 0m;
        var currency = await ResolveTenantCurrencyAsync(tenantId, ct);
        var estimatedCommission = basePrice * commissionRate;

        var images = ResolveAuthorityImages(authoritativeProduct, evt.Images);

        // Get variants from SQL stock authority. Mongo is visibility only and must not
        // decide affiliate inventory availability.
        var variants = await GetProductVariantsAsync(evt.ProductId, tenantId, ct);

        // Calculate total stock
            var totalStock = variants.Sum(v => v.Stock);
        var isOutOfStock = totalStock <= 0;
        // AF-22 HARDENING: Configurable low stock threshold
        var lowStock = totalStock > 0 && totalStock <= _lowStockThreshold;

        var filter = BuildFreshProductEventFilter(evt.ProductId, tenantId, eventId, evt.EventVersion, evt.OccurredAtUtc);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .SetOnInsert(d => d.ProductId, evt.ProductId)
            .SetOnInsert(d => d.TenantId, tenantId)
            .Set(d => d.StoreSlug, storeContext.StoreSlug)
            .Set(d => d.StoreName, storeContext.StoreName)
            .Set(d => d.StoreLogo, storeContext.StoreLogo)
            .Set(d => d.StorefrontUrl, storeContext.StorefrontUrl)
            .Set(d => d.ProductName, authoritativeProduct.Name)
            .Set(d => d.ProductSlug, productSlug)
            .Set(d => d.Description, authoritativeProduct.Description ?? evt.Description ?? "")
            .Set(d => d.Images, images)
            .Set(d => d.BasePrice, basePrice)
            .Set(d => d.Currency, currency)
            .Set(d => d.CommissionRate, commissionRate)
            .Set(d => d.EstimatedCommission, estimatedCommission)
            .Set(d => d.TemplateType, policy?.TemplateType ?? PointOfSale.Core.Constants.LandingTemplates.ClassicV1)
            .Set(d => d.TotalAvailableStock, totalStock)
            .Set(d => d.IsOutOfStock, isOutOfStock)
            .Set(d => d.LowStock, lowStock)
            .Set(d => d.Variants, variants)
            .Set(d => d.LastProductEventVersion, evt.EventVersion)
            .Set(d => d.LastProductEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
            .SetOnInsert(d => d.CreatedAtUtc, DateTime.UtcNow) // AF-34: Freshness boost signal
            .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

        var result = await UpsertProductIntelligenceAsync(
            evt.ProductId,
            tenantId,
            eventId,
            filter,
            update,
            ct);

        if (result.ModifiedCount == 0 && result.UpsertedId == null)
        {
            _logger.LogWarning(
                "[AF-22:ProductCreated] Stale or duplicate product event skipped | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion} | EventId: {EventId}",
                evt.ProductId,
                tenantId,
                evt.EventVersion,
                eventId);
            return;
        }

        _logger.LogInformation(
            "[AF-22:ProductCreated] Upserted product intelligence | ProductId: {ProductId} | TenantId: {TenantId}",
            evt.ProductId, tenantId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT UPDATED
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:ProductUpdated] ProductId: {ProductId} | TenantId: {TenantId} | ChangedFields: {ChangedFields}",
            evt.ProductId, evt.TenantId, string.Join(", ", evt.ChangedFields));

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductUpdated] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;

        if (!await IsProductVisibleInSqlAsync(tenantId, evt.ProductId, ct))
        {
            await DeleteProductIntelligenceIfFreshAsync(
                tenantId,
                evt.ProductId,
                eventId,
                evt.EventVersion,
                evt.OccurredAtUtc,
                ct);
            return;
        }

        // Check idempotency
        var existingDoc = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.ProductId == evt.ProductId && d.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug("[AF-22:ProductUpdated] Event already processed | EventId: {EventId}", eventId);
            return;
        }

        var authoritativeProduct = await LoadCurrentStorefrontAuthorityProductAsync(
            tenantId,
            evt.ProductId,
            evt.Name,
            evt.Description,
            evt.Images,
            evt.OccurredAtUtc,
            ct);
        if (authoritativeProduct == null ||
            !authoritativeProduct.TryResolveAffiliateDiscoveryBasePrice(out var basePrice))
        {
            await DeleteProductIntelligenceIfFreshAsync(
                tenantId,
                evt.ProductId,
                eventId,
                evt.EventVersion,
                evt.OccurredAtUtc,
                ct);
            return;
        }

        var policy = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .Where(p => p.ProductId == evt.ProductId && p.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
        var commissionRate = policy?.CommissionRate ?? existingDoc?.CommissionRate ?? 0m;
        var productSlug = authoritativeProduct.Slug ?? evt.Name?.ToLowerInvariant().Replace(" ", "-") ?? evt.ProductId.ToString();
        var storeContext = await GetStoreContextAsync(tenantId, productSlug, ct);
        if (string.IsNullOrWhiteSpace(storeContext.StorefrontUrl))
        {
            _logger.LogError(
                "[AF-22:ProductUpdated] Missing StorefrontBaseUrl | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId,
                evt.ProductId);
            return;
        }

        var updateBuilder = Builders<AffiliateProductIntelligenceDocument>.Update;
        var update = updateBuilder
            .SetOnInsert(d => d.ProductId, evt.ProductId)
            .SetOnInsert(d => d.TenantId, tenantId)
            .SetOnInsert(d => d.CreatedAtUtc, DateTime.UtcNow)
            .Set(d => d.StoreSlug, storeContext.StoreSlug)
            .Set(d => d.StoreName, storeContext.StoreName)
            .Set(d => d.StoreLogo, storeContext.StoreLogo)
            .Set(d => d.StorefrontUrl, storeContext.StorefrontUrl)
            .Set(d => d.ProductName, authoritativeProduct.Name)
            .Set(d => d.ProductSlug, productSlug)
            .Set(d => d.Description, authoritativeProduct.Description ?? evt.Description ?? string.Empty)
            .Set(d => d.Images, ResolveAuthorityImages(authoritativeProduct, evt.Images))
            .Set(d => d.BasePrice, basePrice)
            .Set(d => d.Currency, await ResolveTenantCurrencyAsync(tenantId, ct))
            .Set(d => d.CommissionRate, commissionRate)
            .Set(d => d.EstimatedCommission, basePrice * commissionRate)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
            .Set(d => d.LastProductEventVersion, evt.EventVersion)
            .Set(d => d.LastProductEventOccurredAtUtc, evt.OccurredAtUtc)
            .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

        var result = await UpsertProductIntelligenceAsync(
            evt.ProductId,
            tenantId,
            eventId,
            BuildFreshProductEventFilter(evt.ProductId, tenantId, eventId, evt.EventVersion, evt.OccurredAtUtc),
            update,
            ct);

        if (result.ModifiedCount == 0 && result.UpsertedId == null)
        {
            _logger.LogWarning(
                "[AF-22:ProductUpdated] Stale or duplicate product event skipped | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion} | EventId: {EventId}",
                evt.ProductId,
                tenantId,
                evt.EventVersion,
                eventId);
            return;
        }

        _logger.LogInformation(
            "[AF-22:ProductUpdated] Updated product intelligence | ProductId: {ProductId} | TenantId: {TenantId}",
            evt.ProductId,
            tenantId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT STOCK CHANGED
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<ProductMediaChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductMediaChanged] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                evt,
                ProductMediaConsumerName,
                typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
                evt.ProductId,
                ct);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[AF-22:ProductMediaChanged] Duplicate or active media replay collapsed before affiliate intelligence side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    evt.EventId,
                    evt.ProductId,
                    tenantId);
                return;
            }

            var images = await LoadCurrentSqlProductImagesForMediaEventAsync(
                tenantId,
                evt.ProductId,
                evt.EventVersion,
                ct);

            if (images == null)
            {
                _logger.LogWarning(
                    "[AF-22:ProductMediaChanged] Rejected stale or corrupted media event by SQL authority | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                    evt.EventId,
                    evt.ProductId,
                    tenantId,
                    evt.EventVersion);

                await fence.CompleteAsync(
                    evt,
                    ProductMediaConsumerName,
                    typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
                    evt.ProductId,
                    ct);
                return;
            }

            var update = Builders<AffiliateProductIntelligenceDocument>.Update
                .Set(d => d.Images, images)
                .Set(d => d.LastProductEventVersion, evt.EventVersion)
                .Set(d => d.LastProductEventOccurredAtUtc, evt.OccurredAtUtc)
                .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
                .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

            var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
                BuildFreshProductEventFilter(evt.ProductId, tenantId, eventId, evt.EventVersion, evt.OccurredAtUtc),
                update,
                new UpdateOptions { IsUpsert = false },
                ct);

            if (result.ModifiedCount == 0)
            {
                _logger.LogInformation(
                    "[AF-22:ProductMediaChanged] Affiliate intelligence media update skipped as stale, duplicate, or not yet projected | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                    evt.ProductId,
                    tenantId,
                    evt.EventId);
            }

            await fence.CompleteAsync(
                evt,
                ProductMediaConsumerName,
                typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
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

    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductDeleted] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        await DeleteProductIntelligenceFromSqlLifecycleAsync(
            evt.TenantId.Value,
            evt.ProductId,
            evt.EventId.ToString(),
            evt.EventVersion,
            evt.OccurredAtUtc,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductArchivedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductArchived] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        await DeleteProductIntelligenceFromSqlLifecycleAsync(
            evt.TenantId.Value,
            evt.ProductId,
            evt.EventId.ToString(),
            evt.EventVersion,
            evt.OccurredAtUtc,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context)
    {
        var evt = context.Message;
        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:ProductPublishStatusChanged] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        await DeleteProductIntelligenceFromSqlLifecycleAsync(
            evt.TenantId.Value,
            evt.ProductId,
            evt.EventId.ToString(),
            evt.EventVersion,
            evt.OccurredAtUtc,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductStockChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:StockChanged] ProductId: {ProductId} | TenantId: {TenantId} | NewQuantity: {NewQuantity}",
            evt.ProductId, evt.TenantId, evt.NewQuantity);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:StockChanged] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;

        // Check idempotency
        var existingDoc = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.ProductId == evt.ProductId && d.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug("[AF-22:StockChanged] Event already processed | EventId: {EventId}", eventId);
            return;
        }

        // Get updated variants from SQL stock authority. The legacy product-level
        // stock event is only a trigger for derivative affiliate visibility.
        var variants = await GetProductVariantsAsync(evt.ProductId, tenantId, ct);

        // Calculate total stock
            var totalStock = variants.Sum(v => v.Stock);
        var isOutOfStock = totalStock <= 0;
        
        // AF-22 HARDENING: Configurable low stock threshold
        var lowStock = totalStock > 0 && totalStock <= _lowStockThreshold;

        var filter = BuildFreshStockEventFilter(evt.ProductId, tenantId, eventId, evt.EventVersion, null);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.TotalAvailableStock, totalStock)
            .Set(d => d.IsOutOfStock, isOutOfStock)
            .Set(d => d.LowStock, lowStock)
            .Set(d => d.Variants, variants)
            .Set(d => d.LastStockEventVersion, evt.EventVersion)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
            .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

        var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
            filter, update, new UpdateOptions { IsUpsert = false }, ct);

        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning(
                "[AF-22:StockChanged] Stale or duplicate stock event skipped | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion} | EventId: {EventId}",
                evt.ProductId,
                tenantId,
                evt.EventVersion,
                eventId);
            return;
        }

        _logger.LogInformation(
            "[AF-22:StockChanged] Updated stock | ProductId: {ProductId} | TotalAvailableStock: {TotalAvailableStock}",
            evt.ProductId, totalStock);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRODUCT VARIANT STOCK CHANGED (AF-22 FIX)
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:VariantStockChanged] VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | NewQuantity: {NewQuantity}",
            evt.VariantId, evt.ProductId, evt.TenantId, evt.NewQuantity);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:VariantStockChanged] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;

        ProjectionConsumerEventFence? fence = null;
        var consumerName = nameof(AffiliateProductIntelligenceProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent);
        var eventType = typeof(ProductVariantStockChangedEvent).FullName ?? nameof(ProductVariantStockChangedEvent);
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                evt,
                consumerName,
                eventType,
                evt.VariantId,
                ct);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[AF-22:VariantStockChanged] Duplicate or active replay collapsed before affiliate intelligence side effects | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    evt.EventId,
                    evt.ProductId,
                    evt.VariantId,
                    tenantId);
                return;
            }

            // Check idempotency
            var existingDoc = await _mongoDbContext.AffiliateProductIntelligence
                .Find(d => d.ProductId == evt.ProductId && d.TenantId == tenantId)
                .FirstOrDefaultAsync(ct);

            if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
            {
                _logger.LogDebug("[AF-22:VariantStockChanged] Event already processed | EventId: {EventId}", eventId);
                await fence.CompleteAsync(evt, consumerName, eventType, evt.VariantId, ct);
                return;
            }

            var sqlStockAuthority = await GetProductVariantsAfterSqlStockAuthorityAsync(
                evt.ProductId,
                tenantId,
                evt.VariantId,
                evt.EventVersion,
                evt.StockUpdatedAtUtc,
                ct);

            if (sqlStockAuthority == null)
            {
                _logger.LogWarning(
                    "[AF-22:VariantStockChanged] Event rejected by SQL stock authority | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventVersion: {EventVersion} | StockUpdatedAtUtc: {StockUpdatedAtUtc}",
                    evt.EventId,
                    evt.ProductId,
                    evt.VariantId,
                    tenantId,
                    evt.EventVersion,
                    evt.StockUpdatedAtUtc);
                await fence.CompleteAsync(evt, consumerName, eventType, evt.VariantId, ct);
                return;
            }

            // ProductDocument is allowed to provide non-authoritative display metadata, but
            // the stock quantity itself comes from the SQL authority snapshot above.
            var stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(evt.StockUpdatedAtUtc);
            var variants = await MergeSqlStockWithProjectedVariantMetadataAsync(
                evt.ProductId,
                tenantId,
                sqlStockAuthority,
                ct);

            var totalStock = variants.Sum(v => v.Stock);
            var isOutOfStock = totalStock <= 0;
            var lowStock = totalStock > 0 && totalStock <= _lowStockThreshold;

            var filter = BuildFreshStockEventFilter(evt.ProductId, tenantId, eventId, evt.EventVersion, evt.StockUpdatedAtUtc);

            var update = Builders<AffiliateProductIntelligenceDocument>.Update
                .Set(d => d.TotalAvailableStock, totalStock)
                .Set(d => d.IsOutOfStock, isOutOfStock)
                .Set(d => d.LowStock, lowStock)
                .Set(d => d.Variants, variants)
                .Set(d => d.LastStockEventVersion, evt.EventVersion)
                .Set(d => d.LastStockUpdatedAtUtc, evt.StockUpdatedAtUtc)
                .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
                .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

            var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
                filter, update, new UpdateOptions { IsUpsert = false }, ct);

            if (result.ModifiedCount == 0)
            {
                _logger.LogWarning(
                    "[AF-22:VariantStockChanged] Stale or duplicate stock event skipped | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventVersion: {EventVersion} | StockUpdatedAtUtc: {StockUpdatedAtUtc} | EventId: {EventId}",
                    evt.ProductId,
                    evt.VariantId,
                    tenantId,
                    evt.EventVersion,
                    evt.StockUpdatedAtUtc,
                    eventId);
                await fence.CompleteAsync(evt, consumerName, eventType, evt.VariantId, ct);
                return;
            }

            await fence.CompleteAsync(evt, consumerName, eventType, evt.VariantId, ct);

            _logger.LogInformation(
                "[AF-22:VariantStockChanged] Updated stock | ProductId: {ProductId} | TotalAvailableStock: {TotalAvailableStock}",
                evt.ProductId, totalStock);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE PRODUCT POLICY CHANGED
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<AffiliateProductPolicyChangedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:PolicyChanged] ProductId: {ProductId} | TenantId: {TenantId} | IsEnabled: {IsEnabled} | CommissionRate: {CommissionRate}",
            evt.ProductId, evt.TenantId, evt.IsEnabled, evt.CommissionRate);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:PolicyChanged] Missing TenantId | ProductId: {ProductId}", evt.ProductId);
            return;
        }

        var tenantId = evt.TenantId.Value;

        // Check idempotency
        var existingDoc = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.ProductId == evt.ProductId && d.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (existingDoc != null && existingDoc.ProcessedEventIds.Contains(eventId))
        {
            _logger.LogDebug("[AF-22:PolicyChanged] Event already processed | EventId: {EventId}", eventId);
            return;
        }

        // AF-22 HARDENING: Cross-tenant safety assertion
        if (existingDoc != null && existingDoc.TenantId != tenantId)
        {
            _logger.LogCritical(
                "[AF-22:PolicyChanged] Tenant mismatch detected | DocTenant: {DocTenant} | EventTenant: {EventTenant} | ProductId: {ProductId}",
                existingDoc.TenantId, tenantId, evt.ProductId);
            throw new InvalidOperationException($"AF-22 tenant mismatch: Document tenant {existingDoc.TenantId} != Event tenant {tenantId}");
        }

        // Recalculate estimated commission
        var estimatedCommission = existingDoc != null ? existingDoc.BasePrice * evt.CommissionRate : 0m;

        var filter = BuildFreshPolicyEventFilter(
            evt.ProductId,
            tenantId,
            eventId,
            evt.EventVersion,
            evt.OccurredAtUtc);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.CommissionRate, evt.CommissionRate)
            .Set(d => d.EstimatedCommission, estimatedCommission)
            .Set(d => d.TemplateType, evt.TemplateType)
            .Set(d => d.AffiliateCategoryId, evt.AffiliateCategoryId)
            .Set(d => d.AffiliateCategoryName, evt.AffiliateCategoryName)
            .Set(d => d.AffiliateCategorySlug, evt.AffiliateCategorySlug)
            .Set(d => d.AffiliateParentCategoryName, evt.AffiliateParentCategoryName)
            .Set(d => d.AffiliateParentCategorySlug, evt.AffiliateParentCategorySlug)
            .Set(d => d.LastPolicyEventVersion, evt.EventVersion)
            .Set(d => d.LastPolicyEventOccurredAtUtc, evt.OccurredAtUtc)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
            .PushEach(d => d.ProcessedEventIds, new[] { eventId }, slice: -500);

        var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
            filter, update, new UpdateOptions { IsUpsert = false }, ct);

        if (result.ModifiedCount == 0)
        {
            _logger.LogWarning(
                "[AF-22:PolicyChanged] Stale or duplicate policy event skipped | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion} | OccurredAtUtc: {OccurredAtUtc} | EventId: {EventId}",
                evt.ProductId,
                tenantId,
                evt.EventVersion,
                evt.OccurredAtUtc,
                eventId);
            return;
        }

        _logger.LogInformation(
            "[AF-22:PolicyChanged] Updated commission | ProductId: {ProductId} | CommissionRate: {CommissionRate} | CategoryId: {CategoryId}",
            evt.ProductId, evt.CommissionRate, evt.AffiliateCategoryId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // STORE PROFILE UPDATED
    // ═══════════════════════════════════════════════════════════════════

    public async Task Consume(ConsumeContext<StoreProfileUpdatedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var eventId = evt.EventId.ToString();

        _logger.LogInformation(
            "[AF-22:StoreProfileUpdated] TenantId: {TenantId} | StoreName: {StoreName} | StoreSlug: {StoreSlug}",
            evt.TenantId, evt.StoreName, evt.StoreSlug);

        if (!evt.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:StoreProfileUpdated] Missing TenantId");
            return;
        }

        var tenantId = evt.TenantId.Value;
        var urlTemplate = _configuration["Storefront:UrlTemplate"];
        if (string.IsNullOrWhiteSpace(urlTemplate))
        {
            _logger.LogError("[AF-22:StoreProfileUpdated] Missing Storefront:UrlTemplate configuration");
            return;
        }

        var storefrontBaseUrl = urlTemplate.Replace("{slug}", evt.StoreSlug);
        var totalUpdated = 0;

        using var cursor = await _mongoDbContext.AffiliateProductIntelligence
            .Find(d => d.TenantId == tenantId)
            .ToCursorAsync(ct);

        while (await cursor.MoveNextAsync(ct))
        {
            var updates = new List<WriteModel<AffiliateProductIntelligenceDocument>>(StoreProfileUpdateBatchSize);

            foreach (var product in cursor.Current)
            {
                var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.And(
                    Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.Id, product.Id),
                    BuildFreshStoreProfileEventFilter(tenantId, eventId, evt.EventVersion, evt.OccurredAtUtc));
                var update = Builders<AffiliateProductIntelligenceDocument>.Update
                    .Set(d => d.StoreSlug, evt.StoreSlug)
                    .Set(d => d.StoreName, evt.StoreName)
                    .Set(d => d.StoreLogo, evt.LogoUrl ?? "")
                    .Set(d => d.StorefrontUrl, $"{storefrontBaseUrl}/products/{product.ProductSlug}")
                    .Set(d => d.LastStoreProfileEventVersion, evt.EventVersion)
                    .Set(d => d.LastStoreProfileEventOccurredAtUtc, evt.OccurredAtUtc)
                    .Set(d => d.LastUpdatedUtc, DateTime.UtcNow)
                    .AddToSet(d => d.ProcessedEventIds, eventId);

                updates.Add(new UpdateOneModel<AffiliateProductIntelligenceDocument>(filter, update));
            }

            if (updates.Count == 0)
            {
                continue;
            }

            await _mongoDbContext.AffiliateProductIntelligence.BulkWriteAsync(
                updates,
                new BulkWriteOptions { IsOrdered = false },
                ct);

            totalUpdated += updates.Count;
        }

        _logger.LogInformation(
            "[AF-22:StoreProfileUpdated] Updated {Count} products | TenantId: {TenantId}",
            totalUpdated, tenantId);
    }

    public async Task Consume(ConsumeContext<TenantCurrencyChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("[AF-22:TenantCurrencyChanged] Missing TenantId");
            return;
        }

        var tenantId = context.Message.TenantId.Value;
        var eventId = context.Message.EventId;
        var ct = context.CancellationToken;

        // IDEMPOTENCY: Use tenant-scoped ProcessedEvent inbox for fan-out events
        // The bounded per-document ProcessedEventIds array (slice: -500) can lose old event IDs,
        // allowing stale currency replays to overwrite current values.
        // For tenant-wide fan-out events, we use the dedicated SQL inbox instead.
        
        // Fast-fail check (optional optimization, not authoritative)
        var alreadyProcessed = await _dbContext.ProcessedEvents
            .AnyAsync(pe => 
                pe.EventId == eventId && 
                pe.TenantId == tenantId &&
                pe.ConsumerName == "AffiliateProductIntelligenceProjectionConsumer" &&
                pe.EventType == "TenantCurrencyChangedEvent",
                ct);

        if (alreadyProcessed)
        {
            _logger.LogDebug(
                "[AF-22:TenantCurrencyChanged] Event already processed (inbox check) | TenantId: {TenantId} | EventId: {EventId}",
                tenantId,
                eventId);
            return;
        }

        var currency = await ResolveTenantCurrencyAsync(tenantId, ct);

        // Update all products for this tenant with the new currency
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(d => d.TenantId, tenantId);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.Currency, currency)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);

        var result = await _mongoDbContext.AffiliateProductIntelligence.UpdateManyAsync(
            filter,
            update,
            cancellationToken: ct);

        // Record successful processing in the inbox
        // Atomic idempotency: rely on unique constraint (TenantId+EventId+ConsumerName+EventType)
        // to prevent duplicate processing in race conditions
        var processedEvent = Core.Entities.ProcessedEvent.Create(
            eventId: eventId,
            consumerName: "AffiliateProductIntelligenceProjectionConsumer",
            eventType: "TenantCurrencyChangedEvent",
            entityId: tenantId, // Use tenantId as the entity for tenant-wide events
            tenantId: tenantId,
            correlationId: context.Message.CorrelationId ?? eventId.ToString());

        _dbContext.ProcessedEvents.Add(processedEvent);
        
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            // Another consumer instance already recorded this event (race condition)
            _logger.LogDebug(
                "[AF-22:TenantCurrencyChanged] Event already processed (duplicate key) | TenantId: {TenantId} | EventId: {EventId}",
                tenantId,
                eventId);
            return;
        }

        _logger.LogInformation(
            "[AF-22:TenantCurrencyChanged] Updated currency for {Count} products | TenantId: {TenantId} | Currency: {Currency}",
            result.ModifiedCount,
            tenantId,
            currency);
    }

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        // PostgreSQL duplicate key error code: 23505
        // SQL Server duplicate key error number: 2601 or 2627
        var innerException = ex.InnerException;
        if (innerException == null)
        {
            return false;
        }

        var message = innerException.Message;
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("23505") || // PostgreSQL
               message.Contains("2601") ||  // SQL Server unique index
               message.Contains("2627");    // SQL Server primary key/unique constraint
    }

    private async Task<UpdateResult> UpsertProductIntelligenceAsync(
        Guid productId,
        Guid tenantId,
        string eventId,
        FilterDefinition<AffiliateProductIntelligenceDocument> filter,
        UpdateDefinition<AffiliateProductIntelligenceDocument> update,
        CancellationToken ct)
    {
        try
        {
            return await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (MongoWriteException ex) when (IsDuplicateMongoKeyException(ex))
        {
            _logger.LogWarning(
                "[AF-22:ProductUpsert] Duplicate-key upsert race recovered with retry | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                productId,
                tenantId,
                eventId);

            return await _mongoDbContext.AffiliateProductIntelligence.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = false },
                ct);
        }
    }

    private static bool IsDuplicateMongoKeyException(MongoWriteException ex) =>
        ex.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
        ex.WriteError?.Code == 11000;

    // ═══════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<string>?> LoadCurrentSqlProductImagesForMediaEventAsync(
        Guid tenantId,
        Guid productId,
        int eventVersion,
        CancellationToken ct)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.Media)
            .Where(item => item.TenantId == tenantId && item.Id == productId)
            .SingleOrDefaultAsync(ct);

        if (product == null || product.IsDeleted || !product.IsActive || !product.IsOnlineVisible)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        if (!ProjectionAuthorityValidator.ValidateSqlVersion(
                sqlVersion,
                eventVersion,
                requireExactVersion: true).IsAccepted)
        {
            return null;
        }

        var images = product.Media
            .Where(media => string.Equals(media.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .Select(media => media.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return images.Count > 0
            ? images
            : ParseImages(product.Images);
    }

    private async Task<bool> IsProductVisibleInSqlAsync(Guid tenantId, Guid productId, CancellationToken ct)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(product =>
                product.TenantId == tenantId &&
                product.Id == productId &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published,
                ct);
    }

    private async Task DeleteProductIntelligenceFromSqlLifecycleAsync(
        Guid tenantId,
        Guid productId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var sqlProduct = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => new
            {
                product.IsDeleted,
                product.IsActive,
                product.IsOnlineVisible,
                product.PublishStatus,
                product.UpdatedAt,
                product.CreatedAt,
                product.ProjectionMutationVersion
            })
            .FirstOrDefaultAsync(ct);

        if (sqlProduct is
            {
                IsDeleted: false,
                IsActive: true,
                IsOnlineVisible: true,
                PublishStatus: PublishStatus.Published
            })
        {
            _logger.LogInformation(
                "[AF-22:ProductLifecycle] Current SQL product authority is visible; stale lifecycle event ignored | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                productId,
                tenantId,
                eventId);
            return;
        }

        var appliedVersion = sqlProduct == null
            ? eventVersion
            : Math.Max(eventVersion, ProductEventVersionHelper.ToProductEventVersion(sqlProduct.ProjectionMutationVersion));
        var appliedOccurredAtUtc = sqlProduct?.UpdatedAt ?? sqlProduct?.CreatedAt ?? occurredAtUtc;

        await DeleteProductIntelligenceIfFreshAsync(
            tenantId,
            productId,
            eventId,
            appliedVersion,
            appliedOccurredAtUtc,
            ct);
    }

    private async Task DeleteProductIntelligenceIfFreshAsync(
        Guid tenantId,
        Guid productId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var result = await _mongoDbContext.AffiliateProductIntelligence.DeleteOneAsync(
            BuildFreshProductEventFilter(productId, tenantId, eventId, eventVersion, occurredAtUtc),
            ct);

        _logger.LogInformation(
            "[AF-22:ProductLifecycle] Product intelligence delete {Result} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion} | EventId: {EventId}",
            result.DeletedCount > 0 ? "applied" : "skipped",
            productId,
            tenantId,
            eventVersion,
            eventId);
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken ct)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(ct);

        return NormalizeCurrency(
            tenantCurrency,
            $"Consumer: AffiliateProductIntelligenceProjectionConsumer, Method: ResolveTenantCurrencyAsync, TenantId: {tenantId}");
    }

    private static string NormalizeCurrency(string? currency, string context) =>
        CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
            currency,
            context);

    private async Task<ProductDocument?> LoadCurrentStorefrontAuthorityProductAsync(
        Guid tenantId,
        Guid productId,
        string? fallbackName,
        string? fallbackDescription,
        string? fallbackImages,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var seed = await _mongoDbContext.Products
            .Find(product => product.TenantId == tenantId && product.ProductId == productId)
            .FirstOrDefaultAsync(ct)
            ?? new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId,
                Name = fallbackName ?? string.Empty,
                Description = fallbackDescription,
                Images = fallbackImages,
                CreatedAtUtc = occurredAtUtc,
                SyncedAtUtc = occurredAtUtc
            };

        return await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            seed,
            tenantId,
            ct,
            preserveProjectedStock: true);
    }

    private List<string> ResolveAuthorityImages(ProductDocument product, string? fallbackImages)
    {
        var authorityImages = (product.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return authorityImages.Count > 0
            ? authorityImages
            : ParseImages(product.Images ?? fallbackImages);
    }

    private async Task<StoreContext> GetStoreContextAsync(Guid tenantId, string productSlug, CancellationToken ct)
    {
        // AF-22 FIX: Use subdomain pattern like storefront with product slug
        var urlTemplate = _configuration["Storefront:UrlTemplate"];
        if (string.IsNullOrWhiteSpace(urlTemplate))
        {
            _logger.LogError("[AF-22] Missing Storefront:UrlTemplate configuration");
            return new StoreContext
            {
                StoreSlug = "",
                StoreName = "",
                StoreLogo = "",
                StorefrontUrl = "" // Will trigger fail-fast guard
            };
        }

        // Get store info from public_affiliate_stores MongoDB collection
        var storeDoc = await _mongoDbContext.PublicAffiliateStores
            .Find(s => s.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (storeDoc != null)
        {
            // Build subdomain URL: http://{slug}.localhost:5174/products/{productSlug}
            var storefrontUrl = urlTemplate.Replace("{slug}", storeDoc.StoreSlug) + $"/products/{productSlug}";

            return new StoreContext
            {
                StoreSlug = storeDoc.StoreSlug,
                StoreName = storeDoc.StoreName,
                StoreLogo = storeDoc.LogoUrl ?? "",
                StorefrontUrl = storefrontUrl
            };
        }

        // Fallback: get from tenant settings
        var tenant = await _dbContext.Tenants.FindAsync(new object[] { tenantId }, ct);
        if (tenant != null)
        {
            var storeSlug = tenant.Name.ToLowerInvariant().Replace(" ", "-");
            var storefrontUrl = urlTemplate.Replace("{slug}", storeSlug) + $"/products/{productSlug}";

            return new StoreContext
            {
                StoreSlug = storeSlug,
                StoreName = tenant.Name,
                StoreLogo = "",
                StorefrontUrl = storefrontUrl
            };
        }

        // AF-22 HARDENING: Return empty context (will trigger fail-fast guard)
        _logger.LogWarning("[AF-22] No store context found for TenantId: {TenantId}", tenantId);
        return new StoreContext
        {
            StoreSlug = "",
            StoreName = "",
            StoreLogo = "",
            StorefrontUrl = ""
        };
    }

    private async Task<List<VariantStockSubDocument>> GetProductVariantsAsync(
        Guid productId, Guid tenantId, CancellationToken ct)
    {
        var sqlVariants = await LoadSqlVariantStockSnapshotsAsync(
            productId,
            tenantId,
            expectedVariantId: null,
            expectedEventVersion: null,
            expectedStockUpdatedAtUtc: null,
            ct);

        return sqlVariants.Count == 0
            ? new List<VariantStockSubDocument>()
            : await MergeSqlStockWithProjectedVariantMetadataAsync(productId, tenantId, sqlVariants, ct);
    }

    private async Task<List<SqlVariantStockSnapshot>?> GetProductVariantsAfterSqlStockAuthorityAsync(
        Guid productId,
        Guid tenantId,
        Guid changedVariantId,
        int expectedEventVersion,
        DateTime expectedStockUpdatedAtUtc,
        CancellationToken ct)
    {
        var sqlVariants = await LoadSqlVariantStockSnapshotsAsync(
            productId,
            tenantId,
            changedVariantId,
            expectedEventVersion,
            expectedStockUpdatedAtUtc,
            ct);

        return sqlVariants.Count == 0 ? null : sqlVariants;
    }

    private async Task<List<SqlVariantStockSnapshot>> LoadSqlVariantStockSnapshotsAsync(
        Guid productId,
        Guid tenantId,
        Guid? expectedVariantId,
        int? expectedEventVersion,
        DateTime? expectedStockUpdatedAtUtc,
        CancellationToken ct)
    {
        var sqlVariantRows = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(variant =>
                variant.ProductId == productId)
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.SortOrder)
            .ThenBy(variant => variant.CreatedAt)
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.Price,
                variant.OnlinePrice,
                Attributes = variant.Attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new SqlVariantAttributeSnapshot(attribute.Value, attribute.SortOrder))
                    .ToList(),
                variant.StockUpdatedAtUtc,
                variant.ProjectionMutationVersion
            })
            .ToListAsync(ct);

        var sqlVariantIds = sqlVariantRows
            .Select(variant => variant.Id)
            .ToList();

        var currentStockByVariant = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => sqlVariantIds.Contains(stock.VariantId))
            .GroupBy(stock => stock.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                AvailableStock = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.AvailableStock, ct);

        var sqlVariants = sqlVariantRows
            .Select(variant => new SqlVariantStockSnapshot(
                variant.Id,
                variant.SKU,
                variant.Price,
                variant.OnlinePrice,
                variant.Attributes,
                Math.Max(0, currentStockByVariant.GetValueOrDefault(variant.Id)),
                variant.StockUpdatedAtUtc,
                ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)))
            .ToList();

        if (expectedVariantId.HasValue)
        {
            var expected = sqlVariants.FirstOrDefault(variant => variant.VariantId == expectedVariantId.Value);
            var expectedUtc = StockProjectionTimestamp.NormalizeUtc(expected?.StockUpdatedAtUtc);
            var incomingUtc = StockProjectionTimestamp.NormalizeUtc(expectedStockUpdatedAtUtc);

            if (expected == null ||
                expected.EventVersion != expectedEventVersion ||
                !expectedUtc.HasValue ||
                expectedUtc.Value != incomingUtc)
            {
                return new List<SqlVariantStockSnapshot>();
            }
        }

        return sqlVariants;
    }

    private async Task<List<VariantStockSubDocument>> MergeSqlStockWithProjectedVariantMetadataAsync(
        Guid productId,
        Guid tenantId,
        IReadOnlyCollection<SqlVariantStockSnapshot> sqlVariants,
        CancellationToken ct)
    {
        var productDoc = await _mongoDbContext.Products
            .Find(p => p.ProductId == productId && p.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        var projectedById = productDoc?.Variants?.ToDictionary(variant => variant.VariantId)
            ?? new Dictionary<Guid, PointOfSale.Infrastructure.MongoDB.Documents.ProductVariantDocument>();

        return sqlVariants.Select(sqlVariant =>
        {
            projectedById.TryGetValue(sqlVariant.VariantId, out var projected);
            var name = projected?.Attributes != null && projected.Attributes.Any()
                ? string.Join(" - ", projected.Attributes.OrderBy(attribute => attribute.SortOrder).Select(attribute => attribute.Value))
                : sqlVariant.Attributes.Count > 0
                    ? string.Join(" - ", sqlVariant.Attributes.OrderBy(attribute => attribute.SortOrder).Select(attribute => attribute.Value))
                    : sqlVariant.Sku ?? "Default";

            var price = projected?.EffectivePrice > 0
                ? projected.EffectivePrice
                : ResolveEffectivePrice(sqlVariant.Price, sqlVariant.OnlinePrice);

            return new VariantStockSubDocument
            {
                VariantId = sqlVariant.VariantId,
                Name = name,
                Price = price,
                Stock = decimal.ToInt32(decimal.Truncate(sqlVariant.AvailableStock))
            };
        }).ToList();
    }

    private static decimal ResolveEffectivePrice(decimal? price, decimal? onlinePrice)
    {
        if (onlinePrice.HasValue && onlinePrice.Value > 0)
        {
            return onlinePrice.Value;
        }

        return price ?? 0m;
    }

    private static FilterDefinition<AffiliateProductIntelligenceDocument> BuildFreshProductEventFilter(
        Guid productId,
        Guid tenantId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc)
    {
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter;
        return filter.And(
            filter.Eq(d => d.ProductId, productId),
            filter.Eq(d => d.TenantId, tenantId),
            filter.Not(filter.AnyEq(d => d.ProcessedEventIds, eventId)),
            filter.Or(
                filter.Exists(d => d.LastProductEventVersion, false),
                filter.Lt(d => d.LastProductEventVersion, eventVersion),
                filter.And(
                    filter.Eq(d => d.LastProductEventVersion, eventVersion),
                    filter.Or(
                        filter.Exists(d => d.LastProductEventOccurredAtUtc, false),
                        filter.Lte(d => d.LastProductEventOccurredAtUtc, occurredAtUtc)))));
    }

    private static FilterDefinition<AffiliateProductIntelligenceDocument> BuildFreshStockEventFilter(
        Guid productId,
        Guid tenantId,
        string eventId,
        int eventVersion,
        DateTime? stockUpdatedAtUtc)
    {
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter;
        var freshnessFilters = new List<FilterDefinition<AffiliateProductIntelligenceDocument>>
        {
            filter.Exists(d => d.LastStockEventVersion, false),
            filter.Lt(d => d.LastStockEventVersion, eventVersion)
        };

        if (stockUpdatedAtUtc.HasValue)
        {
            freshnessFilters.Add(filter.And(
                filter.Eq(d => d.LastStockEventVersion, eventVersion),
                filter.Or(
                    filter.Exists(d => d.LastStockUpdatedAtUtc, false),
                    filter.Lte(d => d.LastStockUpdatedAtUtc, stockUpdatedAtUtc.Value))));
        }

        return filter.And(
            filter.Eq(d => d.ProductId, productId),
            filter.Eq(d => d.TenantId, tenantId),
            filter.Not(filter.AnyEq(d => d.ProcessedEventIds, eventId)),
            filter.Or(freshnessFilters));
    }

    private static FilterDefinition<AffiliateProductIntelligenceDocument> BuildFreshPolicyEventFilter(
        Guid productId,
        Guid tenantId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc)
    {
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter;
        return filter.And(
            filter.Eq(d => d.ProductId, productId),
            filter.Eq(d => d.TenantId, tenantId),
            filter.Not(filter.AnyEq(d => d.ProcessedEventIds, eventId)),
            filter.Or(
                filter.Exists(d => d.LastPolicyEventVersion, false),
                filter.Eq(d => d.LastPolicyEventVersion, 0),
                filter.Lt(d => d.LastPolicyEventVersion, eventVersion),
                filter.And(
                    filter.Eq(d => d.LastPolicyEventVersion, eventVersion),
                    filter.Or(
                        filter.Exists(d => d.LastPolicyEventOccurredAtUtc, false),
                        filter.Eq(d => d.LastPolicyEventOccurredAtUtc, null),
                        filter.Lte(d => d.LastPolicyEventOccurredAtUtc, occurredAtUtc)))));
    }

    private static FilterDefinition<AffiliateProductIntelligenceDocument> BuildFreshStoreProfileEventFilter(
        Guid tenantId,
        string eventId,
        int eventVersion,
        DateTime occurredAtUtc)
    {
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter;
        return filter.And(
            filter.Eq(d => d.TenantId, tenantId),
            filter.Not(filter.AnyEq(d => d.ProcessedEventIds, eventId)),
            filter.Or(
                filter.Exists(d => d.LastStoreProfileEventVersion, false),
                filter.Eq(d => d.LastStoreProfileEventVersion, 0),
                filter.Lt(d => d.LastStoreProfileEventVersion, eventVersion),
                filter.And(
                    filter.Eq(d => d.LastStoreProfileEventVersion, eventVersion),
                    filter.Or(
                        filter.Exists(d => d.LastStoreProfileEventOccurredAtUtc, false),
                        filter.Eq(d => d.LastStoreProfileEventOccurredAtUtc, null),
                        filter.Lte(d => d.LastStoreProfileEventOccurredAtUtc, occurredAtUtc)))));
    }

    private List<string> ParseImages(string? imagesJson)
    {
        if (string.IsNullOrWhiteSpace(imagesJson))
            return new List<string>();

        try
        {
            // Try JSON array format
            if (imagesJson.TrimStart().StartsWith("["))
            {
                var images = System.Text.Json.JsonSerializer.Deserialize<List<string>>(imagesJson);
                return images ?? new List<string>();
            }

            // Try comma-separated format
            return imagesJson.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private class StoreContext
    {
        public string StoreSlug { get; set; } = "";
        public string StoreName { get; set; } = "";
        public string StoreLogo { get; set; } = "";
        public string StorefrontUrl { get; set; } = "";
    }

    private sealed record SqlVariantStockSnapshot(
        Guid VariantId,
        string? Sku,
        decimal? Price,
        decimal? OnlinePrice,
        IReadOnlyList<SqlVariantAttributeSnapshot> Attributes,
        decimal AvailableStock,
        DateTime? StockUpdatedAtUtc,
        int EventVersion);

    private sealed record SqlVariantAttributeSnapshot(string Value, int SortOrder);
}
