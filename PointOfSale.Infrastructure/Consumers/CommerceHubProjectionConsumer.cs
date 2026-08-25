using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.ReadModels.Evolution;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

public class CommerceHubProjectionConsumer :
    IConsumer<CommerceHubListingCreatedEvent>,
    IConsumer<CommerceHubListingUpdatedEvent>,
    IConsumer<CommerceHubListingUnpublishedEvent>,
    IConsumer<ProductVariantStockChangedEvent>,
    IConsumer<ProductMediaChangedEvent>,
    IConsumer<ProductUpdatedEvent>,
    IConsumer<ProductVariantDeletedEvent>,
    IConsumer<VariantDeletedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductArchivedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>,
    IConsumer<ProductVariantUpdatedEvent>,
    IConsumer<VariantPriceUpdatedEvent>,
    IConsumer<PromotionCreatedEvent>,
    IConsumer<PromotionUpdatedEvent>,
    IConsumer<PromotionDeletedEvent>,
    IConsumer<PromotionExpiredEvent>,
    IConsumer<TenantUpdatedEvent>,
    IConsumer<StoreConfigUpdatedEvent>,
    IConsumer<ShippingMethodUpdatedEvent>,
    IConsumer<ShippingMethodDeletedEvent>,
    IConsumer<TenantCurrencyChangedEvent>
{
    private const string VariantStockConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent);
    private const string ProductVariantDeletedConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(ProductVariantDeletedEvent);
    private const string VariantDeletedConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(VariantDeletedEvent);
    private const string VariantUpdatedConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(ProductVariantUpdatedEvent);
    private const string VariantPriceConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(VariantPriceUpdatedEvent);
    private const string ProductMediaConsumerName = nameof(CommerceHubProjectionConsumer) + ":" + nameof(ProductMediaChangedEvent);
    private const string PromotionConsumerName = nameof(CommerceHubProjectionConsumer) + ":Promotion";
    private const string ListingLifecycleConsumerName = nameof(CommerceHubProjectionConsumer) + ":ListingLifecycle";
    private const string StoreMetadataConsumerName = nameof(CommerceHubProjectionConsumer) + ":StoreMetadata";
    private const string ShippingMetadataConsumerName = nameof(CommerceHubProjectionConsumer) + ":ShippingMetadata";
    private const string CurrencyMetadataConsumerName = nameof(CommerceHubProjectionConsumer) + ":CurrencyMetadata";
    private static readonly ICommerceHubPriceAuthority PriceAuthority = new CommerceHubPriceAuthority();

    private readonly MongoDbContext _mongoDbContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ILogger<CommerceHubProjectionConsumer> _logger;

    public CommerceHubProjectionConsumer(
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ILogger<CommerceHubProjectionConsumer> logger)
    {
        _mongoDbContext = mongoDbContext;
        _dbContext = dbContext;
        _searchIndexService = searchIndexService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CommerceHubListingCreatedEvent> context)
    {
        await UpsertListingAsync(
            context.Message,
            nameof(CommerceHubListingCreatedEvent),
            context.Message.ListingId,
            context.Message.TenantId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<CommerceHubListingUpdatedEvent> context)
    {
        await UpsertListingAsync(
            context.Message,
            nameof(CommerceHubListingUpdatedEvent),
            context.Message.ListingId,
            context.Message.TenantId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<CommerceHubListingUnpublishedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "CommerceHub unpublish event rejected due to missing TenantId | ListingId: {ListingId}",
                message.ListingId);
            ThrowMissingTenant(nameof(CommerceHubListingUnpublishedEvent), message.ListingId);
        }

        var tenant = message.TenantId.Value;
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            message,
            ListingLifecycleConsumerName,
            nameof(CommerceHubListingUnpublishedEvent),
            message.ListingId,
            context.CancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "CommerceHub listing unpublish duplicate or active replay collapsed before side effects | EventId: {EventId} | ListingId: {ListingId} | TenantId: {TenantId}",
                message.EventId,
                message.ListingId,
                tenant);
            return;
        }

        var sqlListing = await LoadSqlCommerceHubListingAuthorityAsync(
            tenant,
            message.ListingId,
            context.CancellationToken);
        var authoritativeProductId = sqlListing?.ProductId ?? message.ProductId;
        var authoritativeVariantId = sqlListing?.VariantId ?? message.VariantId;
        var authoritativeCategoryId = sqlListing?.CommerceHubCategoryId ?? message.CategoryId;

        var document = await FindListingAsync(message.ListingId, tenant, context.CancellationToken);
        if (document == null)
        {
            _logger.LogWarning(
                "CommerceHub unpublish event ignored because listing projection was not found | TenantId: {TenantId} | ListingId: {ListingId}",
                tenant,
                message.ListingId);
            await fence.CompleteAsync(
                message,
                ListingLifecycleConsumerName,
                nameof(CommerceHubListingUnpublishedEvent),
                message.ListingId,
                context.CancellationToken);
            return;
        }

        if (document.LastEventId == message.EventId)
        {
            if (RequiresSearchSyncRetry(document))
            {
                await SyncSearchAsync(document, context.CancellationToken);
            }

            return;
        }

        if (IsStaleListingEvent(document, message.OccurredAtUtc, message.EventVersion))
        {
            _logger.LogWarning(
                "Rejected stale CommerceHub listing unpublish projection | TenantId: {TenantId} | ListingId: {ListingId} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion} | LastOccurredAtUtc: {LastOccurredAtUtc} | LastVersion: {LastVersion}",
                message.TenantId.Value,
                message.ListingId,
                message.EventId,
                message.OccurredAtUtc,
                message.EventVersion,
                document.LastListingEventOccurredAtUtc,
                document.LastListingEventVersion);
            return;
        }

        if (sqlListing is { IsDeleted: false, IsPublished: true })
        {
            if (await UpdateListingPublicationVisibilityIfFreshAsync(
                    document.Id,
                    document.TenantId,
                    isPublished: true,
                    message.EventId,
                    message.OccurredAtUtc,
                    message.EventVersion,
                    context.CancellationToken))
            {
                var restored = await FindListingAsync(document.Id, document.TenantId, context.CancellationToken);
                if (restored != null)
                {
                    await SyncSearchAsync(restored, context.CancellationToken);
                }
            }

            await fence.CompleteAsync(
                message,
                ListingLifecycleConsumerName,
                nameof(CommerceHubListingUnpublishedEvent),
                message.ListingId,
                context.CancellationToken);
            return;
        }

        document.IsPublished = false;
        document.ProductId = authoritativeProductId;
        document.VariantId = authoritativeVariantId;
        document.CategoryId = authoritativeCategoryId;
        document.Price = await ResolveCurrentSqlVariantPriceAsync(
            tenant,
            authoritativeProductId,
            authoritativeVariantId,
            document.Price,
            context.CancellationToken);
        document.CommerceHubPriceOverride =
            CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant(tenant)
                ? sqlListing?.CommerceHubPrice
                : null;
        document.PriceSource = document.CommerceHubPriceOverride.HasValue
            ? CommerceHubPriceSource.CommerceHubOverride.ToString()
            : CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice.ToString();
        document.OverridePresent = document.CommerceHubPriceOverride.HasValue;
        document.AttributesJson = sqlListing?.AttributesJson ?? document.AttributesJson;
        document.Attributes = ParseAttributesToBsonDocument(document.AttributesJson);
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.LastEventId = message.EventId;
        document.LastListingEventOccurredAtUtc = message.OccurredAtUtc;
        document.LastListingEventVersion = message.EventVersion;

        MarkSearchSyncPending(document);
        if (await SaveListingDocumentIfFreshAsync(
                document,
                message.EventId,
                message.OccurredAtUtc,
                message.EventVersion,
                context.CancellationToken))
        {
            await SyncSearchAsync(document, context.CancellationToken);
        }

        await fence.CompleteAsync(
            message,
            ListingLifecycleConsumerName,
            nameof(CommerceHubListingUnpublishedEvent),
            message.ListingId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "CommerceHub stock event rejected due to missing TenantId | VariantId: {VariantId}",
                message.VariantId);
            ThrowMissingTenant(nameof(ProductVariantStockChangedEvent), message.VariantId);
        }

        ProjectionConsumerEventFence? fence = null;
        var eventType = typeof(ProductVariantStockChangedEvent).FullName ?? nameof(ProductVariantStockChangedEvent);
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                VariantStockConsumerName,
                eventType,
                message.VariantId,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub variant stock duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    message.EventId,
                    message.VariantId,
                    message.TenantId);
                return;
            }

            var sqlStockAuthority = await LoadSqlVariantStockAuthorityAsync(
                message.TenantId.Value,
                message.ProductId,
                message.VariantId,
                message.EventVersion,
                message.StockUpdatedAtUtc,
                context.CancellationToken);

            if (sqlStockAuthority == null)
            {
                _logger.LogWarning(
                    "CommerceHub variant stock event rejected by SQL authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                    message.EventId,
                    message.VariantId,
                    message.ProductId,
                    message.TenantId,
                    message.EventVersion);

                await fence.CompleteAsync(message, VariantStockConsumerName, eventType, message.VariantId, context.CancellationToken);
                return;
            }

            var listings = await _mongoDbContext.CommerceHubProducts
                .Find(x => x.TenantId == message.TenantId.Value && x.VariantId == message.VariantId)
                .ToListAsync(context.CancellationToken);

            if (listings.Count == 0)
            {
                await fence.CompleteAsync(message, VariantStockConsumerName, eventType, message.VariantId, context.CancellationToken);
                return;
            }

            var availableStock = sqlStockAuthority.TotalStockQuantity;
            var stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(sqlStockAuthority.StockUpdatedAtUtc);
            foreach (var listing in listings)
            {
                if (listing.LastEventId == message.EventId)
                {
                    if (RequiresSearchSyncRetry(listing))
                    {
                        await SyncSearchAsync(listing, context.CancellationToken);
                    }

                    continue;
                }

                if (IsStaleStockProjection(
                        listing.StockUpdatedAtUtc,
                        listing.LastStockEventVersion,
                        stockUpdatedAtUtc,
                        message.EventVersion))
                {
                    _logger.LogWarning(
                        "Rejected stale CommerceHub stock projection update | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId} | ExistingStockUpdatedAtUtc: {ExistingStockUpdatedAtUtc} | ExistingStockEventVersion: {ExistingStockEventVersion} | IncomingStockUpdatedAtUtc: {IncomingStockUpdatedAtUtc} | IncomingStockEventVersion: {IncomingStockEventVersion} | EventId: {EventId}",
                        listing.TenantId,
                        listing.Id,
                        listing.VariantId,
                        listing.StockUpdatedAtUtc,
                        listing.LastStockEventVersion,
                        stockUpdatedAtUtc,
                        message.EventVersion,
                        message.EventId);
                    continue;
                }

                if (await UpdateStockProjectionIfFreshAsync(
                        listing.Id,
                        listing.TenantId,
                        availableStock,
                        stockUpdatedAtUtc,
                        message.EventVersion,
                        message.EventId,
                        context.CancellationToken))
                {
                    var updated = await FindListingAsync(listing.Id, listing.TenantId, context.CancellationToken);
                    if (updated != null)
                    {
                        await SyncSearchAsync(updated, context.CancellationToken);
                    }
                }
            }

            await fence.CompleteAsync(message, VariantStockConsumerName, eventType, message.VariantId, context.CancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            ThrowMissingTenant(nameof(ProductUpdatedEvent), message.ProductId);
        }

        var tenantId = message.TenantId.Value;
        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == message.ProductId)
            .ToListAsync(context.CancellationToken);

        if (listings.Count == 0)
        {
            return;
        }

        var productDocument = await _mongoDbContext.Products
            .Find(x => x.TenantId == tenantId && x.ProductId == message.ProductId)
            .FirstOrDefaultAsync(context.CancellationToken);

        foreach (var listing in listings)
        {
            if (listing.LastEventId == message.EventId)
            {
                if (RequiresSearchSyncRetry(listing))
                {
                    await SyncSearchAsync(listing, context.CancellationToken);
                }

                continue;
            }

            var sqlStockAuthority = await LoadSqlVariantStockAuthorityAsync(
                tenantId,
                message.ProductId,
                listing.VariantId,
                context.CancellationToken);
            var sqlVisibleListing = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new CommerceHubProductDocument
                {
                    Id = listing.Id,
                    TenantId = listing.TenantId
                },
                listing.TenantId,
                context.CancellationToken);
            var productName = sqlVisibleListing?.ProductName ?? productDocument?.Name ?? message.Name ?? listing.ProductName;
            var description = sqlVisibleListing?.Description ?? productDocument?.Description ?? listing.Description;
            var image = sqlVisibleListing?.Image;
            var availableStock = sqlStockAuthority?.TotalStockQuantity ?? 0m;
            var stockUpdatedAtUtc = sqlStockAuthority?.StockUpdatedAtUtc ?? listing.StockUpdatedAtUtc;

            if (await UpdateProductProjectionIfFreshAsync(
                    listing.Id,
                    listing.TenantId,
                    productName,
                    description,
                    image,
                    availableStock,
                    stockUpdatedAtUtc,
                    message.EventId,
                    message.OccurredAtUtc,
                    message.EventVersion,
                    context.CancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, context.CancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, context.CancellationToken);
                }
            }
        }
    }

    public async Task Consume(ConsumeContext<ProductMediaChangedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductMediaChangedEvent rejected due to missing TenantId | ProductId: {ProductId}",
                message.ProductId);
            ThrowMissingTenant(nameof(ProductMediaChangedEvent), message.ProductId);
        }

        var tenantId = message.TenantId.Value;
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            message,
            ProductMediaConsumerName,
            typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
            message.ProductId,
            context.CancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "CommerceHub product media duplicate or active replay collapsed before side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                message.EventId,
                message.ProductId,
                tenantId);
            return;
        }

        if (!await IsCurrentSqlProductMediaEventAsync(
                tenantId,
                message.ProductId,
                message.EventVersion,
                context.CancellationToken))
        {
            _logger.LogWarning(
                "CommerceHub product media event rejected by SQL authority | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                message.EventId,
                message.ProductId,
                tenantId,
                message.EventVersion);

            await fence.CompleteAsync(
                message,
                ProductMediaConsumerName,
                typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
                message.ProductId,
                context.CancellationToken);
            return;
        }

        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == message.ProductId)
            .ToListAsync(context.CancellationToken);

        foreach (var listing in listings)
        {
            var sqlListing = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                listing,
                tenantId,
                context.CancellationToken);

            if (sqlListing == null)
            {
                await _searchIndexService.DeleteAsync(
                    listing.TenantId,
                    listing.Id,
                    cancellationToken: context.CancellationToken);
                continue;
            }

            if (await UpdateListingImageProjectionIfFreshAsync(
                    listing.Id,
                    tenantId,
                    sqlListing.Image,
                    message.EventId,
                    message.OccurredAtUtc,
                    message.EventVersion,
                    context.CancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, tenantId, context.CancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, context.CancellationToken);
                }
            }
            else if (listing.LastEventId == message.EventId && RequiresSearchSyncRetry(listing))
            {
                await SyncSearchAsync(listing, context.CancellationToken);
            }
        }

        await fence.CompleteAsync(
            message,
            ProductMediaConsumerName,
            typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent),
            message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductVariantDeletedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductVariantDeletedEvent rejected due to missing TenantId | VariantId: {VariantId}",
                message.VariantId);
            ThrowMissingTenant(nameof(ProductVariantDeletedEvent), message.VariantId);
        }

        await UnpublishListingsByVariantWithFenceAsync(
            message,
            ProductVariantDeletedConsumerName,
            typeof(ProductVariantDeletedEvent).FullName ?? nameof(ProductVariantDeletedEvent),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<VariantDeletedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "VariantDeletedEvent rejected due to missing TenantId | VariantId: {VariantId}",
                message.VariantId);
            ThrowMissingTenant(nameof(VariantDeletedEvent), message.VariantId);
        }

        await UnpublishListingsByVariantWithFenceAsync(
            message,
            VariantDeletedConsumerName,
            typeof(VariantDeletedEvent).FullName ?? nameof(VariantDeletedEvent),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductDeletedEvent rejected due to missing TenantId | ProductId: {ProductId}",
                message.ProductId);
            ThrowMissingTenant(nameof(ProductDeletedEvent), message.ProductId);
        }

        await ReconcileListingsByProductPublicationAsync(
            message,
            typeof(ProductDeletedEvent).FullName ?? nameof(ProductDeletedEvent),
            message.TenantId.Value,
            message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductArchivedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductArchivedEvent rejected due to missing TenantId | ProductId: {ProductId}",
                message.ProductId);
            ThrowMissingTenant(nameof(ProductArchivedEvent), message.ProductId);
        }

        await ReconcileListingsByProductPublicationAsync(
            message,
            typeof(ProductArchivedEvent).FullName ?? nameof(ProductArchivedEvent),
            message.TenantId.Value,
            message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductPublishStatusChangedEvent rejected due to missing TenantId | ProductId: {ProductId}",
                message.ProductId);
            ThrowMissingTenant(nameof(ProductPublishStatusChangedEvent), message.ProductId);
        }

        await ReconcileListingsByProductPublicationAsync(
            message,
            typeof(ProductPublishStatusChangedEvent).FullName ?? nameof(ProductPublishStatusChangedEvent),
            message.TenantId.Value,
            message.ProductId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductVariantUpdatedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "ProductVariantUpdatedEvent rejected due to missing TenantId | VariantId: {VariantId}",
                message.VariantId);
            ThrowMissingTenant(nameof(ProductVariantUpdatedEvent), message.VariantId);
        }

        await ReconcileListingsByVariantLifecycleAsync(
            message,
            typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent),
            message.TenantId.Value,
            message.ProductId,
            message.VariantId,
            message.OccurredAtUtc,
            message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<VariantPriceUpdatedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue)
        {
            _logger.LogWarning(
                "VariantPriceUpdatedEvent rejected due to missing TenantId | VariantId: {VariantId}",
                message.VariantId);
            ThrowMissingTenant(nameof(VariantPriceUpdatedEvent), message.VariantId);
        }

        ProjectionConsumerEventFence? fence = null;
        var eventType = typeof(VariantPriceUpdatedEvent).FullName ?? nameof(VariantPriceUpdatedEvent);
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                VariantPriceConsumerName,
                eventType,
                message.VariantId,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub variant price duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    message.EventId,
                    message.VariantId,
                    message.TenantId);
                return;
            }

            var sqlPrice = await LoadSqlVariantPriceAuthorityAsync(
                message.TenantId.Value,
                message.ProductId,
                message.VariantId,
                message.EventVersion,
                context.CancellationToken);

            if (!sqlPrice.HasValue)
            {
                _logger.LogWarning(
                    "CommerceHub variant price event rejected by SQL authority | EventId: {EventId} | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                    message.EventId,
                    message.VariantId,
                    message.ProductId,
                    message.TenantId,
                    message.EventVersion);

                await fence.CompleteAsync(
                    message,
                    VariantPriceConsumerName,
                    eventType,
                    message.VariantId,
                    context.CancellationToken);
                return;
            }

            await UpdateFallbackPriceAsync(
                message.TenantId.Value,
                message.ProductId,
                message.VariantId,
                sqlPrice.Value,
                message.EventId,
                message.OccurredAtUtc,
                message.EventVersion,
                context.CancellationToken);

            await fence.CompleteAsync(
                message,
                VariantPriceConsumerName,
                eventType,
                message.VariantId,
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
        await RefreshPromotionAdjustedPricesWithFenceAsync(
            context.Message,
            typeof(PromotionCreatedEvent).FullName ?? nameof(PromotionCreatedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionUpdatedEvent> context)
    {
        await RefreshPromotionAdjustedPricesWithFenceAsync(
            context.Message,
            typeof(PromotionUpdatedEvent).FullName ?? nameof(PromotionUpdatedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionDeletedEvent> context)
    {
        await RefreshPromotionAdjustedPricesWithFenceAsync(
            context.Message,
            typeof(PromotionDeletedEvent).FullName ?? nameof(PromotionDeletedEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PromotionExpiredEvent> context)
    {
        await RefreshPromotionAdjustedPricesWithFenceAsync(
            context.Message,
            typeof(PromotionExpiredEvent).FullName ?? nameof(PromotionExpiredEvent),
            context.Message.TenantId,
            context.Message.Scope,
            context.Message.AppliesToId,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<TenantUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("TenantUpdatedEvent rejected due to missing TenantId");
            ThrowMissingTenant(nameof(TenantUpdatedEvent), context.Message.EventId);
        }

        await UpdateStoreNameWithFenceAsync(
            context.Message,
            nameof(TenantUpdatedEvent),
            context.Message.TenantId.Value,
            context.Message.TenantId.Value,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<StoreConfigUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("StoreConfigUpdatedEvent rejected due to missing TenantId");
            ThrowMissingTenant(nameof(StoreConfigUpdatedEvent), context.Message.EventId);
        }

        await UpdateStoreNameWithFenceAsync(
            context.Message,
            nameof(StoreConfigUpdatedEvent),
            context.Message.TenantId.Value,
            context.Message.StoreConfigId,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ShippingMethodUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("ShippingMethodUpdatedEvent rejected due to missing TenantId");
            ThrowMissingTenant(nameof(ShippingMethodUpdatedEvent), context.Message.EventId);
        }

        await UpdateStoreShippingFeeWithFenceAsync(
            context.Message,
            nameof(ShippingMethodUpdatedEvent),
            context.Message.TenantId.Value,
            context.Message.ShippingMethodId,
            context.Message.EventId,
            context.Message.BaseRate,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ShippingMethodDeletedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("ShippingMethodDeletedEvent rejected due to missing TenantId");
            ThrowMissingTenant(nameof(ShippingMethodDeletedEvent), context.Message.EventId);
        }

        await UpdateStoreShippingFeeWithFenceAsync(
            context.Message,
            nameof(ShippingMethodDeletedEvent),
            context.Message.TenantId.Value,
            context.Message.ShippingMethodId,
            context.Message.EventId,
            null,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<TenantCurrencyChangedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            _logger.LogWarning("TenantCurrencyChangedEvent rejected due to missing TenantId");
            ThrowMissingTenant(nameof(TenantCurrencyChangedEvent), context.Message.EventId);
        }

        await UpdateCurrencyWithFenceAsync(
            context.Message,
            nameof(TenantCurrencyChangedEvent),
            context.Message.TenantId.Value,
            context.Message.TenantId.Value,
            context.Message.EventId,
            context.Message.OccurredAtUtc,
            context.Message.EventVersion,
            context.CancellationToken);
    }

    private async Task UpsertListingAsync(
        IntegrationEvent @event,
        string eventType,
        Guid listingId,
        Guid? tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            _logger.LogWarning(
                "CommerceHub listing event rejected due to missing TenantId | ListingId: {ListingId}",
                listingId);
            ThrowMissingTenant("CommerceHubListingEvent", listingId);
        }

        if (listingId == Guid.Empty)
        {
            _logger.LogWarning(
                "CommerceHub listing event ignored due to invalid listing identifier | TenantId: {TenantId} | ListingId: {ListingId}",
                tenantId.Value,
                listingId);
            return;
        }

        var tenant = tenantId.Value;
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            ListingLifecycleConsumerName,
            eventType,
            listingId,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "CommerceHub listing duplicate or active replay collapsed before side effects | EventType: {EventType} | TenantId: {TenantId} | ListingId: {ListingId} | EventId: {EventId}",
                eventType,
                tenant,
                listingId,
                eventId);
            return;
        }

        var existing = await FindListingAsync(listingId, tenant, cancellationToken);
        if (existing?.LastEventId == eventId)
        {
            if (RequiresSearchSyncRetry(existing))
            {
                await SyncSearchAsync(existing, cancellationToken);
            }

            await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
            return;
        }

        if (existing != null && IsStaleListingEvent(existing, occurredAtUtc, eventVersion))
        {
            _logger.LogWarning(
                "Rejected stale CommerceHub listing projection | TenantId: {TenantId} | ListingId: {ListingId} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion} | LastOccurredAtUtc: {LastOccurredAtUtc} | LastVersion: {LastVersion}",
                tenant,
                listingId,
                eventId,
                occurredAtUtc,
                eventVersion,
                existing.LastListingEventOccurredAtUtc,
                existing.LastListingEventVersion);
            await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
            return;
        }

        var sqlListing = await LoadSqlCommerceHubListingAuthorityAsync(
            tenant,
            listingId,
            cancellationToken);

        if (sqlListing == null || sqlListing.IsDeleted)
        {
            if (existing != null &&
                await UnpublishListingIfFreshAsync(
                    existing,
                    eventId,
                    occurredAtUtc,
                    eventVersion,
                    cancellationToken))
            {
                var unpublished = await FindListingAsync(existing.Id, existing.TenantId, cancellationToken);
                if (unpublished != null)
                {
                    await SyncSearchAsync(unpublished, cancellationToken);
                }
            }

            await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
            return;
        }

        var productId = sqlListing.ProductId;
        var variantId = sqlListing.VariantId;
        var categoryId = sqlListing.CommerceHubCategoryId;
        if (productId == Guid.Empty || variantId == Guid.Empty || categoryId == Guid.Empty)
        {
            _logger.LogWarning(
                "CommerceHub listing event rejected by SQL authority due to invalid identifiers | TenantId: {TenantId} | ListingId: {ListingId} | ProductId: {ProductId} | VariantId: {VariantId} | CategoryId: {CategoryId}",
                tenant,
                listingId,
                productId,
                variantId,
                categoryId);
            await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
            return;
        }

        var productDocument = await _mongoDbContext.Products
            .Find(x => x.TenantId == tenant && x.ProductId == productId)
            .FirstOrDefaultAsync(cancellationToken);
        var storefrontConfig = await _mongoDbContext.StorefrontConfigs
            .Find(x => x.TenantId == tenant)
            .FirstOrDefaultAsync(cancellationToken);
        var categoryMetadata = await ResolveCommerceHubCategoryMetadataAsync(categoryId, cancellationToken);

        var variantDocument = productDocument?.Variants.FirstOrDefault(x => x.VariantId == variantId);
        if (productDocument == null || variantDocument == null)
        {
            _logger.LogWarning(
                "CommerceHub projection is using partial data because the product read model is incomplete | TenantId: {TenantId} | ListingId: {ListingId} | ProductFound: {ProductFound} | VariantFound: {VariantFound}",
                tenant,
                listingId,
                productDocument != null,
                variantDocument != null);
        }

        if (categoryMetadata == null)
        {
            _logger.LogWarning(
                "CommerceHub listing projection rejected because current SQL category lineage is not visible | TenantId: {TenantId} | ListingId: {ListingId} | CategoryId: {CategoryId}",
                tenant,
                listingId,
                categoryId);
            if (existing != null &&
                await UnpublishListingIfFreshAsync(
                    existing,
                    eventId,
                    occurredAtUtc,
                    eventVersion,
                    cancellationToken))
            {
                var unpublished = await FindListingAsync(existing.Id, existing.TenantId, cancellationToken);
                if (unpublished != null)
                {
                    await SyncSearchAsync(unpublished, cancellationToken);
                }
            }

            await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
            return;
        }

        var document = existing ?? new CommerceHubProductDocument
        {
            Id = listingId,
            TenantId = tenant,
            CreatedAtUtc = DateTime.UtcNow
        };

        document.TenantId = tenant;
        document.ProductId = productId;
        document.VariantId = variantId;
        document.StoreName = ResolveStoreName(storefrontConfig?.StoreName, existing?.StoreName);
        document.ProductName = productDocument?.Name ?? existing?.ProductName ?? "Unknown Product";
        document.Description = productDocument?.Description ?? existing?.Description;
        // Public media is populated only after the SQL authority overlay below accepts current media lineage.
        document.Image = null;
        document.Price = await ResolveCurrentSqlVariantPriceAsync(
            tenant,
            productId,
            variantId,
            existing?.Price ?? 0m,
            cancellationToken);
        document.CommerceHubPriceOverride =
            CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant(tenant)
                ? sqlListing.CommerceHubPrice
                : null;
        document.PriceSource = document.CommerceHubPriceOverride.HasValue
            ? CommerceHubPriceSource.CommerceHubOverride.ToString()
            : CommerceHubPriceSource.ProductVariantEffectiveOnlinePrice.ToString();
        document.OverridePresent = document.CommerceHubPriceOverride.HasValue;
        document.CategoryId = categoryId;
        document.CategoryName = categoryMetadata?.Name ?? existing?.CategoryName;
        document.CategoryPath = categoryMetadata?.Path ?? existing?.CategoryPath;
        document.Currency = ResolveCurrency();
        document.StoreShippingFee = await ResolveStoreShippingFeeAsync(tenant, existing?.StoreShippingFee, null, cancellationToken);
        var sqlStockAuthority = await LoadSqlVariantStockAuthorityAsync(
            tenant,
            productId,
            variantId,
            cancellationToken);

        document.AvailableStock = sqlStockAuthority?.TotalStockQuantity ?? 0m;
        document.InStock = document.AvailableStock > 0;
        document.StockUpdatedAtUtc = sqlStockAuthority?.StockUpdatedAtUtc ?? existing?.StockUpdatedAtUtc;
        document.IsPublished = sqlListing.IsPublished;
        document.AttributesJson = sqlListing.AttributesJson;
        document.Attributes = ParseAttributesToBsonDocument(sqlListing.AttributesJson);
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.LastEventId = eventId;
        document.LastListingEventOccurredAtUtc = occurredAtUtc;
        document.LastListingEventVersion = eventVersion;

        var sqlVisibleDocument = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            document,
            tenant,
            cancellationToken);
        if (sqlVisibleDocument == null)
        {
            document.IsPublished = false;
        }
        else
        {
            document = sqlVisibleDocument;
        }

        MarkSearchSyncPending(document);
        if (await SaveListingDocumentIfFreshAsync(document, eventId, occurredAtUtc, eventVersion, cancellationToken))
        {
            await SyncSearchAsync(document, cancellationToken);
        }

        await fence.CompleteAsync(@event, ListingLifecycleConsumerName, eventType, listingId, cancellationToken);
    }

    private async Task RefreshPromotionAdjustedPricesWithFenceAsync(
        IntegrationEvent @event,
        string eventType,
        Guid? tenantId,
        string? scope,
        Guid? productId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue ||
            !string.Equals(scope, "product", StringComparison.OrdinalIgnoreCase) ||
            !productId.HasValue)
        {
            return;
        }

        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                PromotionConsumerName,
                eventType,
                ResolvePromotionEventEntityId(@event),
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub promotion duplicate or active replay collapsed before listing/search side effects | EventId: {EventId} | EventType: {EventType} | TenantId: {TenantId}",
                    @event.EventId,
                    eventType,
                    tenantId.Value);
                return;
            }

            await RefreshPromotionAdjustedPricesAsync(
                tenantId,
                scope,
                productId,
                cancellationToken);

            await fence.CompleteAsync(
                @event,
                PromotionConsumerName,
                eventType,
                ResolvePromotionEventEntityId(@event),
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task RefreshPromotionAdjustedPricesAsync(
        Guid? tenantId,
        string? scope,
        Guid? productId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue ||
            !string.Equals(scope, "product", StringComparison.OrdinalIgnoreCase) ||
            !productId.HasValue)
        {
            return;
        }

        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(listing => listing.TenantId == tenantId.Value && listing.ProductId == productId.Value)
            .ToListAsync(cancellationToken);
        if (listings.Count == 0)
        {
            return;
        }

        var variantPrices = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId.Value)
            .Where(variant => variant.ProductId == productId.Value)
            .Select(variant => new
            {
                variant.Id,
                variant.Price,
                variant.OnlinePrice
            })
            .ToListAsync(cancellationToken);

        var resolvedVariantPrices = new Dictionary<Guid, decimal>();
        foreach (var variantPrice in variantPrices)
        {
            var baseVariantPrice = CommerceHubPriceAuthority.ResolveBaseVariantPrice(
                variantPrice.Price,
                variantPrice.OnlinePrice);
            var priceResolution = await PriceAuthority.ResolveAsync(
                baseVariantPrice,
                overridePrice: null,
                cancellationToken);
            resolvedVariantPrices[variantPrice.Id] = priceResolution.FinalPrice;
        }

        foreach (var listing in listings)
        {
            if (!resolvedVariantPrices.TryGetValue(listing.VariantId, out var basePrice))
            {
                continue;
            }

            // Storefront promotions are storefront-only overlays. CommerceHub
            // numeric pricing remains canonical/channel-owned.
            listing.Price = basePrice;
            var sqlVisibleListing = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                listing,
                tenantId.Value,
                cancellationToken);
            if (sqlVisibleListing == null)
            {
                continue;
            }

            var convergedListing = sqlVisibleListing;
            convergedListing.UpdatedAtUtc = DateTime.UtcNow;
            MarkSearchSyncPending(convergedListing);

            if (await SaveListingDocumentIfFreshAsync(
                    convergedListing,
                    convergedListing.LastEventId ?? Guid.Empty,
                    convergedListing.LastListingEventOccurredAtUtc ?? convergedListing.CreatedAtUtc,
                    convergedListing.LastListingEventVersion,
                    cancellationToken))
            {
                await SyncSearchAsync(convergedListing, cancellationToken);
            }
        }
    }

    private static Guid ResolvePromotionEventEntityId(IntegrationEvent @event) =>
        @event switch
        {
            PromotionCreatedEvent created => created.PromotionId,
            PromotionUpdatedEvent updated => updated.PromotionId,
            PromotionDeletedEvent deleted => deleted.PromotionId,
            PromotionExpiredEvent expired => expired.PromotionId,
            _ => @event.EventId
        };

    private async Task<decimal?> LoadSqlVariantPriceAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(item =>
                item.ProductId == productId &&
                item.Id == variantId)
            .Select(item => new
            {
                item.Price,
                item.OnlinePrice,
                item.ProjectionMutationVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);
        if (sqlVersion != eventVersion)
        {
            return null;
        }

        var baseVariantPrice = CommerceHubPriceAuthority.ResolveBaseVariantPrice(
            variant.Price,
            variant.OnlinePrice);
        var effectivePrice = (await PriceAuthority.ResolveAsync(
            tenantId,
            baseVariantPrice,
            await _dbContext.CommerceHubListings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(listing =>
                    listing.TenantId == tenantId &&
                    listing.VariantId == variantId &&
                    !listing.IsDeleted)
                .Select(listing => listing.CommerceHubPrice)
                .SingleOrDefaultAsync(cancellationToken),
            cancellationToken)).FinalPrice;

        return effectivePrice > 0 ? effectivePrice : null;
    }

    private async Task<CommerceHubListing?> LoadSqlCommerceHubListingAuthorityAsync(
        Guid tenantId,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                listing => listing.TenantId == tenantId && listing.Id == listingId,
                cancellationToken);
    }

    private async Task<decimal> ResolveCurrentSqlVariantPriceAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        decimal fallbackPrice,
        CancellationToken cancellationToken)
    {
        var variantPrice = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(variant =>
                variant.ProductId == productId &&
                variant.Id == variantId)
            .Select(variant => new
            {
                variant.Price,
                variant.OnlinePrice
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variantPrice == null)
        {
            return Math.Max(0m, fallbackPrice);
        }

        var baseVariantPrice = CommerceHubPriceAuthority.ResolveBaseVariantPrice(
            variantPrice.Price,
            variantPrice.OnlinePrice);
        var overridePrice = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                listing.TenantId == tenantId &&
                listing.ProductId == productId &&
                listing.VariantId == variantId &&
                !listing.IsDeleted)
            .Select(listing => listing.CommerceHubPrice)
            .SingleOrDefaultAsync(cancellationToken);
        var priceResolution = await PriceAuthority.ResolveAsync(
            tenantId,
            baseVariantPrice,
            overridePrice,
            cancellationToken);

        return priceResolution.FinalPrice > 0m
            ? priceResolution.FinalPrice
            : Math.Max(0m, fallbackPrice);
    }

    private static void ThrowMissingTenant(string eventName, object eventIdentifier)
    {
        throw new InvalidOperationException(
            $"{eventName} is missing TenantId. CommerceHub projection events are authoritative and must be retried or dead-lettered instead of being silently ignored. EventIdentifier={eventIdentifier}");
    }

    private async Task UpdateFallbackPriceAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        decimal newPrice,
        Guid eventId,
        DateTime eventTimestamp,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == productId && x.VariantId == variantId)
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            // Skip if event already processed
            if (listing.LastEventId == eventId)
            {
                continue;
            }

            // Version is the authority boundary. A lower-version replay must fail closed even when
            // transport timing makes it arrive later than the newer committed price projection.
            if (IsStalePriceProjection(
                    listing.LastPriceEventTimestamp,
                    listing.LastPriceEventVersion,
                    eventTimestamp,
                    eventVersion))
            {
                _logger.LogDebug(
                    "Skipping stale price event | ListingId: {ListingId}, EventTimestamp: {EventTimestamp}, LastPriceEventTimestamp: {LastPriceEventTimestamp}, EventVersion: {EventVersion}, LastPriceEventVersion: {LastPriceEventVersion}",
                    listing.Id,
                    eventTimestamp,
                    listing.LastPriceEventTimestamp,
                    eventVersion,
                    listing.LastPriceEventVersion);
                continue;
            }

            // PHASE CH-2 FIX: Always call UpdatePriceProjectionIfFreshAsync even when price is unchanged
            // This ensures the projection's timestamp/version advances, preventing stale delayed events
            // from overwriting the projection later. Previously, we skipped the update when price matched,
            // which left the projection vulnerable to out-of-order event delivery.
            if (await UpdatePriceProjectionIfFreshAsync(
                    listing.Id,
                    listing.TenantId,
                    newPrice,
                    eventId,
                    eventTimestamp,
                    eventVersion,
                    cancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, cancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, cancellationToken);
                }
            }
        }
    }

    private async Task UpdateStoreNameWithFenceAsync(
        IntegrationEvent message,
        string eventType,
        Guid tenantId,
        Guid entityId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                StoreMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub store metadata duplicate or active replay collapsed before Mongo/search side effects | EventType: {EventType} | TenantId: {TenantId} | EventId: {EventId}",
                    eventType,
                    tenantId,
                    eventId);
                return;
            }

            var currentSqlStoreName = await ResolveCurrentSqlStoreNameAsync(tenantId, cancellationToken);
            await UpdateStoreNameAsync(
                tenantId,
                currentSqlStoreName,
                eventId,
                occurredAtUtc,
                eventVersion,
                cancellationToken);

            await fence.CompleteAsync(
                message,
                StoreMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task UpdateStoreNameAsync(
        Guid tenantId,
        string? storeName,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var normalizedStoreName = string.IsNullOrWhiteSpace(storeName) ? null : storeName.Trim();
        if (normalizedStoreName == null)
        {
            _logger.LogWarning(
                "CommerceHub store sync ignored due to empty StoreName | TenantId: {TenantId}",
                tenantId);
            return;
        }

        // =================================================================
        // SCALABILITY FIX (Audit CH-2): Atomic UpdateManyAsync
        // Previously loaded ALL listings into memory and updated one-by-one.
        // Now uses a single UpdateManyAsync — O(1) memory, O(1) DB call.
        // =================================================================
        // PHASE CH-2 FIX: Always update freshness watermarks even for no-op events
        // Removed Filter.Ne check so freshness fields advance even when StoreName is unchanged,
        // preventing stale delayed events from overwriting the projection later.
        var filter = BuildFreshMetadataEventFilter(
            tenantId,
            eventId,
            occurredAtUtc,
            eventVersion,
            nameof(CommerceHubProductDocument.LastStoreMetadataEventOccurredAtUtc),
            nameof(CommerceHubProductDocument.LastStoreMetadataEventVersion));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.StoreName, normalizedStoreName)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastStoreMetadataEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastStoreMetadataEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts
            .UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "CommerceHub store name updated | TenantId: {TenantId} | StoreName: {StoreName} | Modified: {Modified}",
                tenantId, normalizedStoreName, result.ModifiedCount);

            // Bulk re-sync search for affected listings
            await BulkSyncSearchForTenantAsync(tenantId, cancellationToken);
        }
    }

    private async Task<string?> ResolveCurrentSqlStoreNameAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var storeConfigName = await _dbContext.StoreConfigs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(config => config.TenantId == tenantId)
            .Select(config => config.StoreName)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(storeConfigName))
        {
            return storeConfigName.Trim();
        }

        var tenantName = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(tenantName) ? null : tenantName.Trim();
    }

    private async Task UpdateCurrencyWithFenceAsync(
        IntegrationEvent message,
        string eventType,
        Guid tenantId,
        Guid entityId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                CurrencyMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub currency metadata duplicate or active replay collapsed before Mongo/search side effects | EventType: {EventType} | TenantId: {TenantId} | EventId: {EventId}",
                    eventType,
                    tenantId,
                    eventId);
                return;
            }

            await UpdateCurrencyAsync(
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                cancellationToken);

            await fence.CompleteAsync(
                message,
                CurrencyMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task UpdateCurrencyAsync(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = ResolveCurrency();

        // PHASE CH-2 FIX: Always update freshness watermarks even for no-op events
        // Removed Filter.Ne check so freshness fields advance even when Currency is unchanged,
        // preventing stale delayed events from overwriting the projection later.
        var filter = BuildFreshMetadataEventFilter(
            tenantId,
            eventId,
            occurredAtUtc,
            eventVersion,
            nameof(CommerceHubProductDocument.LastCurrencyEventOccurredAtUtc),
            nameof(CommerceHubProductDocument.LastCurrencyEventVersion));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.Currency, normalizedCurrency)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastCurrencyEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastCurrencyEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts
            .UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "CommerceHub currency updated | TenantId: {TenantId} | Currency: {Currency} | Modified: {Modified}",
                tenantId, normalizedCurrency, result.ModifiedCount);

            await BulkSyncSearchForTenantAsync(tenantId, cancellationToken);
        }
    }

    private async Task UpdateStoreShippingFeeWithFenceAsync(
        IntegrationEvent message,
        string eventType,
        Guid tenantId,
        Guid entityId,
        Guid eventId,
        decimal? eventBaseRate,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                ShippingMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub shipping metadata duplicate or active replay collapsed before Mongo/search side effects | EventType: {EventType} | TenantId: {TenantId} | EventId: {EventId}",
                    eventType,
                    tenantId,
                    eventId);
                return;
            }

            await UpdateStoreShippingFeeAsync(
                tenantId,
                eventId,
                eventBaseRate,
                occurredAtUtc,
                eventVersion,
                cancellationToken);

            await fence.CompleteAsync(
                message,
                ShippingMetadataConsumerName,
                eventType,
                entityId,
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task UpdateStoreShippingFeeAsync(
        Guid tenantId,
        Guid eventId,
        decimal? eventBaseRate,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        _ = eventBaseRate;
        var nextStoreShippingFee = await ResolveStoreShippingFeeAsync(tenantId, null, eventBaseRate, cancellationToken);

        // PHASE CH-2 FIX: Always update freshness watermarks even for no-op events
        // Removed Filter.Ne check so freshness fields advance even when StoreShippingFee is unchanged,
        // preventing stale delayed events from overwriting the projection later.
        var filter = BuildFreshMetadataEventFilter(
            tenantId,
            eventId,
            occurredAtUtc,
            eventVersion,
            nameof(CommerceHubProductDocument.LastShippingEventOccurredAtUtc),
            nameof(CommerceHubProductDocument.LastShippingEventVersion));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.StoreShippingFee, nextStoreShippingFee)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastShippingEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastShippingEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts
            .UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "CommerceHub shipping fee updated | TenantId: {TenantId} | ShippingFee: {ShippingFee} | Modified: {Modified}",
                tenantId, nextStoreShippingFee, result.ModifiedCount);

            await BulkSyncSearchForTenantAsync(tenantId, cancellationToken);
        }
    }

    /// <summary>
    /// Bulk re-indexes all published listings for a tenant after a batch field update.
    /// Uses cursor-based iteration to avoid loading all documents into memory at once.
    /// </summary>
    private async Task BulkSyncSearchForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var pendingFilter = Builders<CommerceHubProductDocument>.Filter.And(
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending));

            // Use cursor-based iteration with a batch size to limit memory usage
            const int batchSize = 100;
            var findOptions = new FindOptions<CommerceHubProductDocument> { BatchSize = batchSize };
            using var cursor = await _mongoDbContext.CommerceHubProducts
                .FindAsync(pendingFilter, findOptions, cancellationToken);

            var syncCount = 0;
            var totalFailedCount = 0;
            const int maxSampleSize = 10;
            var syncFailureSummaries = new List<string>();
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var listing in cursor.Current)
                {
                    try
                    {
                        await SyncSearchAsync(listing, cancellationToken);
                        syncCount++;
                    }
                    catch (Exception ex)
                    {
                        totalFailedCount++;
                        if (syncFailureSummaries.Count < maxSampleSize)
                        {
                            syncFailureSummaries.Add($"[{listing.Id}] {ex.GetType().Name}: {ex.Message}");
                        }
                        _logger.LogError(ex,
                            "CommerceHub search sync failed for individual listing during bulk sync | TenantId: {TenantId} | ListingId: {ListingId} | DocumentId: {DocumentId}",
                            tenantId, listing.Id, listing.Id);

                        // PRODUCTION HARDENING: Increment retry counter and set terminal failure status when threshold reached
                        // This prevents infinite retries for persistently failing documents
                        try
                        {
                            var failureFilter = Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listing.Id);
                            var retryUpdate = Builders<CommerceHubProductDocument>.Update
                                .Inc(x => x.SearchRetryCount, 1)
                                .Set(x => x.SearchLastAttemptedAtUtc, DateTime.UtcNow);

                            var result = await _mongoDbContext.CommerceHubProducts
                                .FindOneAndUpdateAsync(
                                    failureFilter,
                                    retryUpdate,
                                    new FindOneAndUpdateOptions<CommerceHubProductDocument>
                                    {
                                        ReturnDocument = ReturnDocument.After
                                    },
                                    cancellationToken: cancellationToken);

                            // If retry count exceeds threshold, mark as terminal failure
                            if (result != null && result.SearchRetryCount >= CommerceHubProductDocument.MaxSearchRetries)
                            {
                                var terminalUpdate = Builders<CommerceHubProductDocument>.Update
                                    .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncTerminalFailure)
                                    .Set(x => x.SearchLastError, 
                                        $"Terminal failure after {result.SearchRetryCount} retries: {(ex.Message.Length > 400 ? ex.Message[..400] : ex.Message)}")
                                    .Set(x => x.SearchLastAttemptedAtUtc, DateTime.UtcNow);

                                await _mongoDbContext.CommerceHubProducts
                                    .UpdateOneAsync(failureFilter, terminalUpdate, cancellationToken: cancellationToken);

                                _logger.LogError(
                                    "CommerceHub search sync reached terminal failure threshold | TenantId: {TenantId} | ListingId: {ListingId} | RetryCount: {RetryCount}",
                                    tenantId, listing.Id, result.SearchRetryCount);
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogError(retryEx,
                                "Failed to update search retry counter | TenantId: {TenantId} | ListingId: {ListingId}",
                                tenantId, listing.Id);
                        }
                    }
                }
            }

            _logger.LogInformation(
                "CommerceHub bulk search sync completed | TenantId: {TenantId} | Synced: {Synced} | Failed: {Failed}",
                tenantId, syncCount, totalFailedCount);

            // PRODUCTION HARDENING: Rethrow collected failures to trigger message redelivery
            if (totalFailedCount > 0)
            {
                var sampleInfo = syncFailureSummaries.Count < totalFailedCount
                    ? $" (showing first {syncFailureSummaries.Count} of {totalFailedCount})"
                    : string.Empty;
                var failureMessage = $"CommerceHub search sync failed for {totalFailedCount} listing(s){sampleInfo}. Message will be redelivered.\n" +
                                   string.Join("\n", syncFailureSummaries);
                throw new InvalidOperationException(failureMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CommerceHub bulk search sync failed | TenantId: {TenantId}",
                tenantId);

            // PRODUCTION HARDENING: Rethrow to trigger message redelivery instead of swallowing
            throw;
        }
    }

    private async Task UnpublishListingsByVariantWithFenceAsync(
        IntegrationEvent message,
        string consumerName,
        string eventType,
        CancellationToken cancellationToken)
    {
        var variantId = message switch
        {
            ProductVariantDeletedEvent productVariantDeleted => productVariantDeleted.VariantId,
            VariantDeletedEvent variantDeleted => variantDeleted.VariantId,
            _ => Guid.Empty
        };

        var productId = message switch
        {
            ProductVariantDeletedEvent productVariantDeleted => productVariantDeleted.ProductId,
            VariantDeletedEvent variantDeleted => variantDeleted.ProductId,
            _ => Guid.Empty
        };

        if (!message.TenantId.HasValue || variantId == Guid.Empty)
        {
            ThrowMissingTenant(eventType, productId == Guid.Empty ? variantId : productId);
        }

        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                message,
                consumerName,
                eventType,
                variantId,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "CommerceHub variant delete duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    message.EventId,
                    variantId,
                    message.TenantId);
                return;
            }

            await UnpublishListingsByVariantAsync(
                message.TenantId.Value,
                variantId,
                message.EventId,
                message.OccurredAtUtc,
                message.EventVersion,
                cancellationToken);

            await fence.CompleteAsync(
                message,
                consumerName,
                eventType,
                variantId,
                cancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task<SqlVariantStockAuthoritySnapshot?> LoadSqlVariantStockAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(item =>
                item.ProductId == productId &&
                item.Id == variantId)
            .Select(item => new
            {
                item.StockUpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var sqlStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(variant.StockUpdatedAtUtc);
        if (!sqlStockUpdatedAtUtc.HasValue)
        {
            return null;
        }

        var totalStockQuantity = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => stock.VariantId == variantId)
            .Select(stock => stock.Quantity - stock.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);

        return new SqlVariantStockAuthoritySnapshot(
            Math.Max(0, totalStockQuantity),
            sqlStockUpdatedAtUtc.Value);
    }

    private async Task<SqlVariantStockAuthoritySnapshot?> LoadSqlVariantStockAuthorityAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        int eventVersion,
        DateTime incomingStockUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        var variant = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(item =>
                item.ProductId == productId &&
                item.Id == variantId)
            .Select(item => new
            {
                item.ProjectionMutationVersion,
                item.StockUpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);
        var sqlStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(variant.StockUpdatedAtUtc);
        var incomingUtc = StockProjectionTimestamp.NormalizeUtc(incomingStockUpdatedAtUtc);

        if (sqlVersion != eventVersion ||
            !sqlStockUpdatedAtUtc.HasValue ||
            sqlStockUpdatedAtUtc.Value != incomingUtc)
        {
            return null;
        }

        var totalStockQuantity = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => stock.VariantId == variantId)
            .Select(stock => stock.Quantity - stock.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);

        return new SqlVariantStockAuthoritySnapshot(
            Math.Max(0, totalStockQuantity),
            sqlStockUpdatedAtUtc.Value);
    }

    private async Task UnpublishListingsByVariantAsync(
        Guid tenantId,
        Guid variantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.VariantId == variantId)
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            if (!listing.IsPublished)
            {
                continue;
            }

            if (await UnpublishListingIfFreshAsync(listing, eventId, occurredAtUtc, eventVersion, cancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, cancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, cancellationToken);
                }
            }
        }
    }

    private async Task UnpublishListingsByProductAsync(
        Guid tenantId,
        Guid productId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == productId)
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            if (!listing.IsPublished)
            {
                continue;
            }

            if (await UnpublishListingIfFreshAsync(listing, eventId, occurredAtUtc, eventVersion, cancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, cancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, cancellationToken);
                }
            }
        }
    }

    private async Task ReconcileListingsByProductPublicationAsync(
        IntegrationEvent @event,
        string eventType,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            ProductLifecycleConsumerName(),
            eventType,
            productId,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "CommerceHub product publication duplicate or active replay collapsed before side effects | EventId: {EventId} | EventType: {EventType} | ProductId: {ProductId} | TenantId: {TenantId}",
                @event.EventId,
                eventType,
                productId,
                tenantId);
            return;
        }

        var productAuthority = await LoadSqlProductPublicationAuthorityAsync(
            tenantId,
            productId,
            @event.OccurredAtUtc,
            @event.EventVersion,
            cancellationToken);

        var sqlListings = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => listing.TenantId == tenantId && listing.ProductId == productId)
            .Select(listing => new
            {
                listing.Id,
                listing.IsPublished,
                listing.IsDeleted
            })
            .ToDictionaryAsync(listing => listing.Id, cancellationToken);

        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == productId)
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            var sqlListingPublished =
                sqlListings.TryGetValue(listing.Id, out var sqlListing) &&
                !sqlListing.IsDeleted &&
                sqlListing.IsPublished;
            var shouldPublish = productAuthority.IsVisible && sqlListingPublished;

            if (await UpdateListingPublicationVisibilityIfFreshAsync(
                    listing.Id,
                    listing.TenantId,
                    shouldPublish,
                    @event.EventId,
                    productAuthority.OccurredAtUtc,
                    productAuthority.EventVersion,
                    cancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, cancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, cancellationToken);
                }
            }
        }

        await fence.CompleteAsync(
            @event,
            ProductLifecycleConsumerName(),
            eventType,
            productId,
            cancellationToken);
    }

    private async Task ReconcileListingsByVariantLifecycleAsync(
        IntegrationEvent @event,
        string eventType,
        Guid tenantId,
        Guid productId,
        Guid variantId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            VariantUpdatedConsumerName,
            eventType,
            variantId,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "CommerceHub variant lifecycle duplicate or active replay collapsed before side effects | EventId: {EventId} | VariantId: {VariantId} | TenantId: {TenantId}",
                @event.EventId,
                variantId,
                tenantId);
            return;
        }

        var sqlVariantVisible = await ProductVariantLifecycleAuthority.IsPubliclyVisibleAsync(
            _dbContext,
            tenantId,
            productId,
            variantId,
            cancellationToken);

        var sqlListings = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                listing.TenantId == tenantId &&
                listing.ProductId == productId &&
                listing.VariantId == variantId)
            .Select(listing => new
            {
                listing.Id,
                listing.IsPublished,
                listing.IsDeleted
            })
            .ToDictionaryAsync(listing => listing.Id, cancellationToken);

        var listings = await _mongoDbContext.CommerceHubProducts
            .Find(x => x.TenantId == tenantId && x.ProductId == productId && x.VariantId == variantId)
            .ToListAsync(cancellationToken);

        foreach (var listing in listings)
        {
            var sqlListingPublished =
                sqlListings.TryGetValue(listing.Id, out var sqlListing) &&
                !sqlListing.IsDeleted &&
                sqlListing.IsPublished;
            var shouldPublish = sqlVariantVisible && sqlListingPublished;

            if (await UpdateListingPublicationVisibilityIfFreshAsync(
                    listing.Id,
                    listing.TenantId,
                    shouldPublish,
                    @event.EventId,
                    occurredAtUtc,
                    eventVersion,
                    cancellationToken))
            {
                var updated = await FindListingAsync(listing.Id, listing.TenantId, cancellationToken);
                if (updated != null)
                {
                    await SyncSearchAsync(updated, cancellationToken);
                }
            }
        }

        await fence.CompleteAsync(
            @event,
            VariantUpdatedConsumerName,
            eventType,
            variantId,
            cancellationToken);
    }

    private async Task<ProductPublicationAuthoritySnapshot> LoadSqlProductPublicationAuthorityAsync(
        Guid tenantId,
        Guid productId,
        DateTime fallbackOccurredAtUtc,
        int fallbackEventVersion,
        CancellationToken cancellationToken)
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
                item.PublishStatus,
                item.UpdatedAt,
                item.CreatedAt,
                item.ProjectionMutationVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return new ProductPublicationAuthoritySnapshot(false, fallbackOccurredAtUtc, fallbackEventVersion);
        }

        var isVisible =
            !product.IsDeleted &&
            product.IsActive &&
            product.IsOnlineVisible &&
            product.PublishStatus == PublishStatus.Published;
        var eventVersion = Math.Max(
            fallbackEventVersion,
            ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion));
        var occurredAtUtc = product.UpdatedAt ?? product.CreatedAt;

        return new ProductPublicationAuthoritySnapshot(isVisible, occurredAtUtc, eventVersion);
    }

    private static string ProductLifecycleConsumerName() =>
        nameof(CommerceHubProjectionConsumer) + ":ProductLifecycle";

    private async Task<CommerceHubProductDocument?> FindListingAsync(
        Guid listingId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await _mongoDbContext.CommerceHubProducts
            .Find(x => x.Id == listingId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> UpdateStockProjectionIfFreshAsync(
        Guid listingId,
        Guid tenantId,
        decimal availableStock,
        DateTime stockUpdatedAtUtc,
        int eventVersion,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(stockUpdatedAtUtc);

        var filter = Builders<CommerceHubProductDocument>.Filter;
        var stockFreshnessFilter = filter.Or(
            filter.Exists(x => x.StockUpdatedAtUtc, false),
            filter.Eq(x => x.StockUpdatedAtUtc, null),
            filter.Lt(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc),
            filter.And(
                filter.Eq(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc),
                filter.Or(
                    filter.Exists(nameof(CommerceHubProductDocument.LastStockEventVersion), false),
                    filter.Lte(x => x.LastStockEventVersion, eventVersion))));

        var updateFilter = filter.And(
            filter.Eq(x => x.Id, listingId),
            filter.Eq(x => x.TenantId, tenantId),
            filter.Ne(x => x.LastEventId, eventId),
            stockFreshnessFilter);

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.AvailableStock, availableStock)
            .Set(x => x.InStock, availableStock > 0)
            .Set(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc)
            .Set(x => x.LastStockEventVersion, eventVersion)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            updateFilter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private static bool IsStaleStockProjection(
        DateTime? existingStockUpdatedAtUtc,
        int existingStockEventVersion,
        DateTime incomingStockUpdatedAtUtc,
        int incomingStockEventVersion)
    {
        return ProjectionAuthorityValidator.IsIncomingStockStale(
            existingStockUpdatedAtUtc,
            existingStockEventVersion,
            incomingStockUpdatedAtUtc,
            incomingStockEventVersion);
    }

    private async Task<bool> UpdateProductProjectionIfFreshAsync(
        Guid listingId,
        Guid tenantId,
        string productName,
        string? description,
        string? image,
        decimal availableStock,
        DateTime? stockUpdatedAtUtc,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter.And(
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listingId),
            BuildFreshMetadataEventFilter(
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                nameof(CommerceHubProductDocument.LastProductProjectionEventOccurredAtUtc),
                nameof(CommerceHubProductDocument.LastProductProjectionEventVersion)));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.ProductName, productName)
            .Set(x => x.Description, description)
            .Set(x => x.Image, image)
            .Set(x => x.AvailableStock, availableStock)
            .Set(x => x.InStock, availableStock > 0)
            .Set(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastProductProjectionEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastProductProjectionEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private async Task<bool> UpdateListingImageProjectionIfFreshAsync(
        Guid listingId,
        Guid tenantId,
        string? image,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter.And(
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listingId),
            BuildFreshMetadataEventFilter(
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                nameof(CommerceHubProductDocument.LastProductProjectionEventOccurredAtUtc),
                nameof(CommerceHubProductDocument.LastProductProjectionEventVersion)));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.Image, image)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastProductProjectionEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastProductProjectionEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private async Task<bool> IsCurrentSqlProductMediaEventAsync(
        Guid tenantId,
        Guid productId,
        int eventVersion,
        CancellationToken cancellationToken)
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
            .SingleOrDefaultAsync(cancellationToken);

        if (product == null || product.IsDeleted || !product.IsActive || !product.IsOnlineVisible)
        {
            return false;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        return ProjectionAuthorityValidator.ValidateSqlVersion(
            sqlVersion,
            eventVersion,
            requireExactVersion: true).IsAccepted;
    }

    private async Task<bool> UpdatePriceProjectionIfFreshAsync(
        Guid listingId,
        Guid tenantId,
        decimal newPrice,
        Guid eventId,
        DateTime eventTimestamp,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var overridePrice = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => listing.Id == listingId && listing.TenantId == tenantId && !listing.IsDeleted)
            .Select(listing => listing.CommerceHubPrice)
            .SingleOrDefaultAsync(cancellationToken);
        var priceResolution = await PriceAuthority.ResolveAsync(
            tenantId,
            newPrice,
            overridePrice,
            cancellationToken);
        var filter = Builders<CommerceHubProductDocument>.Filter.And(
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listingId),
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CommerceHubProductDocument>.Filter.Ne(x => x.LastEventId, eventId),
            Builders<CommerceHubProductDocument>.Filter.Or(
                Builders<CommerceHubProductDocument>.Filter.Exists(x => x.LastPriceEventVersion, false),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastPriceEventVersion, 0),
                Builders<CommerceHubProductDocument>.Filter.Lt(x => x.LastPriceEventVersion, eventVersion),
                Builders<CommerceHubProductDocument>.Filter.And(
                    Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastPriceEventVersion, eventVersion),
                    Builders<CommerceHubProductDocument>.Filter.Or(
                        Builders<CommerceHubProductDocument>.Filter.Exists(x => x.LastPriceEventTimestamp, false),
                        Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastPriceEventTimestamp, null),
                        Builders<CommerceHubProductDocument>.Filter.Lte(x => x.LastPriceEventTimestamp, eventTimestamp)))));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.Price, priceResolution.FinalPrice)
            .Set(x => x.CommerceHubPriceOverride, priceResolution.OverridePrice)
            .Set(x => x.PriceSource, priceResolution.PriceSource.ToString())
            .Set(x => x.OverridePresent, priceResolution.OverridePrice.HasValue)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastPriceEventTimestamp, eventTimestamp)
            .Set(x => x.LastPriceEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private static bool IsStalePriceProjection(
        DateTime? lastPriceEventTimestamp,
        int lastPriceEventVersion,
        DateTime incomingEventTimestamp,
        int incomingEventVersion)
    {
        return ProjectionAuthorityValidator.IsIncomingEventStale(
            lastPriceEventTimestamp,
            lastPriceEventVersion,
            incomingEventTimestamp,
            incomingEventVersion,
            rejectSameVersion: false);
    }

    private async Task<bool> UnpublishListingIfFreshAsync(
        CommerceHubProductDocument listing,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter.And(
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listing.Id),
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, listing.TenantId),
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true),
            BuildFreshMetadataEventFilter(
                listing.TenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                nameof(CommerceHubProductDocument.LastProductLifecycleEventOccurredAtUtc),
                nameof(CommerceHubProductDocument.LastProductLifecycleEventVersion)),
            Builders<CommerceHubProductDocument>.Filter.Or(
                Builders<CommerceHubProductDocument>.Filter.Exists(x => x.LastListingEventOccurredAtUtc, false),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastListingEventOccurredAtUtc, null),
                Builders<CommerceHubProductDocument>.Filter.Lt(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                Builders<CommerceHubProductDocument>.Filter.And(
                    Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                    Builders<CommerceHubProductDocument>.Filter.Lte(x => x.LastListingEventVersion, eventVersion))));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.IsPublished, false)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastProductLifecycleEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastProductLifecycleEventVersion, eventVersion)
            .Set(x => x.LastListingEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastListingEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private async Task<bool> UpdateListingPublicationVisibilityIfFreshAsync(
        Guid listingId,
        Guid tenantId,
        bool isPublished,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter.And(
            Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, listingId),
            BuildFreshMetadataEventFilter(
                tenantId,
                eventId,
                occurredAtUtc,
                eventVersion,
                nameof(CommerceHubProductDocument.LastProductLifecycleEventOccurredAtUtc),
                nameof(CommerceHubProductDocument.LastProductLifecycleEventVersion)),
            Builders<CommerceHubProductDocument>.Filter.Or(
                Builders<CommerceHubProductDocument>.Filter.Exists(x => x.LastListingEventOccurredAtUtc, false),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastListingEventOccurredAtUtc, null),
                Builders<CommerceHubProductDocument>.Filter.Lt(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                Builders<CommerceHubProductDocument>.Filter.And(
                    Builders<CommerceHubProductDocument>.Filter.Eq(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                    Builders<CommerceHubProductDocument>.Filter.Lte(x => x.LastListingEventVersion, eventVersion))));

        var update = Builders<CommerceHubProductDocument>.Update
            .Set(x => x.IsPublished, isPublished)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, eventId)
            .Set(x => x.LastProductLifecycleEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastProductLifecycleEventVersion, eventVersion)
            .Set(x => x.LastListingEventOccurredAtUtc, occurredAtUtc)
            .Set(x => x.LastListingEventVersion, eventVersion)
            .Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending)
            .Set(x => x.SearchLastError, (string?)null);

        var result = await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    private static FilterDefinition<CommerceHubProductDocument> BuildFreshMetadataEventFilter(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        string occurredAtFieldName,
        string versionFieldName)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Ne(x => x.LastEventId, eventId),
            filter.Or(
                filter.Exists(occurredAtFieldName, false),
                filter.Eq<DateTime?>(occurredAtFieldName, null),
                filter.Lt(occurredAtFieldName, occurredAtUtc),
                filter.And(
                    filter.Eq(occurredAtFieldName, occurredAtUtc),
                    filter.Lt(versionFieldName, eventVersion))));
    }

    private async Task<bool> SaveListingDocumentIfFreshAsync(
        CommerceHubProductDocument document,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        ReadModelEvolutionMetadata.StampCurrent(
            document,
            ReadModelEvolutionRegistry.CommerceHubProduct,
            ReadModelStore.MongoDb);

        var filter = BuildFreshListingEventFilter(document.Id, document.TenantId, eventId, occurredAtUtc, eventVersion);

        try
        {
            var result = await _mongoDbContext.CommerceHubProducts.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            if (result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null)
            {
                return true;
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another consumer won the race with a fresher listing event.
        }

        _logger.LogWarning(
            "CommerceHub listing projection write skipped because a newer listing event is already stored | TenantId: {TenantId} | ListingId: {ListingId} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion}",
            document.TenantId,
            document.Id,
            eventId,
            occurredAtUtc,
            eventVersion);
        return false;
    }

    private static FilterDefinition<CommerceHubProductDocument> BuildFreshListingEventFilter(
        Guid listingId,
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.Id, listingId),
            filter.Eq(x => x.TenantId, tenantId),
            filter.Or(
                filter.Exists(nameof(CommerceHubProductDocument.LastListingEventOccurredAtUtc), false),
                filter.Eq(x => x.LastListingEventOccurredAtUtc, null),
                filter.Lt(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(x => x.LastListingEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(x => x.LastListingEventVersion, eventVersion)),
                filter.Eq(x => x.LastEventId, eventId)));
    }

    private static bool IsStaleListingEvent(
        CommerceHubProductDocument document,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return document.LastListingEventOccurredAtUtc.HasValue &&
               (document.LastListingEventOccurredAtUtc.Value > occurredAtUtc ||
                (document.LastListingEventOccurredAtUtc.Value == occurredAtUtc &&
                 document.LastListingEventVersion >= eventVersion));
    }

    private async Task SyncSearchAsync(CommerceHubProductDocument document, CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTime.UtcNow;
        var errorMessage = "Elasticsearch sync failed. See CommerceHubSearchIndexService logs for details.";
        bool succeeded;

        try
        {
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                document,
                document.TenantId,
                cancellationToken);

            succeeded = sqlAuthority == null
                ? await _searchIndexService.DeleteAsync(document.TenantId, document.Id, cancellationToken: cancellationToken)
                : await _searchIndexService.IndexAsync(
                    CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                    cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            succeeded = false;
            errorMessage = ex.Message;

            _logger.LogError(
                ex,
                "CommerceHub search sync threw unexpectedly | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId}",
                document.TenantId,
                document.Id,
                document.VariantId);
        }

        if (!succeeded)
        {
            _logger.LogError(
                "CommerceHub search sync failed | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId} | CategoryId: {CategoryId} | SearchSyncStatus: {SearchSyncStatus}",
                document.TenantId,
                document.Id,
                document.VariantId,
                document.CategoryId,
                document.SearchSyncStatus);
        }

        await UpdateSearchSyncStatusAsync(
            document.Id,
            document.TenantId,
            attemptedAtUtc,
            succeeded,
            succeeded ? null : errorMessage,
            cancellationToken);

        if (!succeeded)
        {
            throw new InvalidOperationException(
                $"CommerceHub search sync failed for listing {document.Id} and will be retried by message redelivery.");
        }
    }

    private static bool RequiresSearchSyncRetry(CommerceHubProductDocument document) =>
        document.SearchSyncStatus is CommerceHubProductDocument.SearchSyncFailed or CommerceHubProductDocument.SearchSyncPending
        && document.SearchRetryCount < CommerceHubProductDocument.MaxSearchRetries;

    private async Task UpdateSearchSyncStatusAsync(
        Guid listingId,
        Guid tenantId,
        DateTime attemptedAtUtc,
        bool succeeded,
        string? error,
        CancellationToken cancellationToken)
    {
        var updates = new List<UpdateDefinition<CommerceHubProductDocument>>
        {
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastAttemptedAtUtc, attemptedAtUtc),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastError, succeeded ? null : error),
            Builders<CommerceHubProductDocument>.Update.Set(
                x => x.SearchSyncStatus,
                succeeded ? CommerceHubProductDocument.SearchSyncSynced : CommerceHubProductDocument.SearchSyncFailed)
        };

        if (succeeded)
        {
            updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastSucceededAtUtc, attemptedAtUtc));
            updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.LastSyncedAt, attemptedAtUtc));
            updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchRetryCount, 0));
        }

        await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            x => x.Id == listingId && x.TenantId == tenantId,
            Builders<CommerceHubProductDocument>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    private static bool ContainsPriceChange(IEnumerable<string>? changedFields) =>
        changedFields?.Any(x =>
            string.Equals(x, "Price", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, "OnlinePrice", StringComparison.OrdinalIgnoreCase)) == true;

    private static decimal ResolveAvailableStock(
        ProductVariantDocument? variantDocument,
        decimal? existingStock,
        DateTime? existingStockUpdatedAtUtc)
    {
        var variantStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(variantDocument?.StockUpdatedAtUtc);
        var existingUtc = StockProjectionTimestamp.NormalizeUtc(existingStockUpdatedAtUtc);

        if (variantStockUpdatedAtUtc.HasValue &&
            (!existingStockUpdatedAtUtc.HasValue ||
             variantStockUpdatedAtUtc.Value >= existingUtc!.Value))
        {
            return Math.Max(0, variantDocument.TotalStock);
        }

        if (variantDocument != null &&
            !variantDocument.StockUpdatedAtUtc.HasValue &&
            !existingStockUpdatedAtUtc.HasValue)
        {
            return Math.Max(0, variantDocument.TotalStock);
        }

        return Math.Max(0, existingStock ?? 0);
    }

    private static DateTime? ResolveStockUpdatedAtUtc(
        DateTime? candidateStockUpdatedAtUtc,
        DateTime? existingStockUpdatedAtUtc)
    {
        if (!candidateStockUpdatedAtUtc.HasValue)
        {
            return existingStockUpdatedAtUtc;
        }

        if (!existingStockUpdatedAtUtc.HasValue)
        {
            return candidateStockUpdatedAtUtc;
        }

        var candidateUtc = StockProjectionTimestamp.NormalizeUtc(candidateStockUpdatedAtUtc.Value);
        var existingUtc = StockProjectionTimestamp.NormalizeUtc(existingStockUpdatedAtUtc.Value);

        return candidateUtc >= existingUtc
            ? candidateUtc
            : existingUtc;
    }

    private static string ResolveStoreName(string? candidateStoreName, string? existingStoreName)
    {
        if (!string.IsNullOrWhiteSpace(candidateStoreName))
        {
            return candidateStoreName.Trim();
        }

        return string.IsNullOrWhiteSpace(existingStoreName) ? "Unknown Store" : existingStoreName;
    }

    private static string ResolveCurrency() =>
        PointOfSale.Core.Financial.FinancialCurrencyGuard.PlatformBaseCurrencyCode;

    private async Task<decimal> ResolveStoreShippingFeeAsync(
        Guid tenantId,
        decimal? existingStoreShippingFee,
        decimal? eventBaseRate,
        CancellationToken cancellationToken)
    {
        _ = eventBaseRate;

        var shippingMethod = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.BaseRate,
                x.FreeShippingThreshold
            })
            .Where(x => x.FreeShippingThreshold == null || x.BaseRate > 0m)
            .FirstOrDefaultAsync(cancellationToken);

        if (shippingMethod != null)
        {
            return Math.Max(0, shippingMethod.BaseRate);
        }

        return Math.Max(0, existingStoreShippingFee ?? 0);
    }

    private static void MarkSearchSyncPending(CommerceHubProductDocument document)
    {
        document.SearchSyncStatus = CommerceHubProductDocument.SearchSyncPending;
        document.SearchLastError = null;
    }

    private async Task<CommerceHubCategoryMetadata?> ResolveCommerceHubCategoryMetadataAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync(
            _dbContext,
            categoryId,
            cancellationToken);

        return category == null
            ? null
            : new CommerceHubCategoryMetadata(category.Name, category.PathText);
    }

    /// <summary>
    /// Parses normalized AttributesJson into a typed BsonDocument.
    /// Preserves numeric types as BsonDouble, strings as BsonString.
    /// Returns null if input is null/empty (no attributes).
    /// </summary>
    private static BsonDocument? ParseAttributesToBsonDocument(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson)) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attributesJson);
            if (parsed == null || parsed.Count == 0) return null;

            var doc = new BsonDocument();
            foreach (var kvp in parsed)
            {
                BsonValue bsonValue = kvp.Value.ValueKind switch
                {
                    JsonValueKind.Number => new BsonDouble(kvp.Value.GetDouble()),
                    JsonValueKind.String => new BsonString(kvp.Value.GetString() ?? string.Empty),
                    JsonValueKind.True => new BsonBoolean(true),
                    JsonValueKind.False => new BsonBoolean(false),
                    _ => new BsonString(kvp.Value.ToString())
                };
                doc.Add(kvp.Key, bsonValue);
            }
            return doc;
        }
        catch
        {
            return null;
        }
    }

    private sealed record CommerceHubCategoryMetadata(string Name, string Path);

    private sealed record SqlVariantStockAuthoritySnapshot(decimal TotalStockQuantity, DateTime StockUpdatedAtUtc);

    private sealed record ProductPublicationAuthoritySnapshot(
        bool IsVisible,
        DateTime OccurredAtUtc,
        int EventVersion);
}

public class CommerceHubProjectionConsumerDefinition : ConsumerDefinition<CommerceHubProjectionConsumer>
{
    public CommerceHubProjectionConsumerDefinition()
    {
        EndpointName = "commercehub-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CommerceHubProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
