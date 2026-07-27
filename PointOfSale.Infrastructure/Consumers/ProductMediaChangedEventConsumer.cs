using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

public class ProductMediaChangedEventConsumer : IConsumer<ProductMediaChangedEvent>
{
    private const string ConsumerName = nameof(ProductMediaChangedEventConsumer);

    private readonly IMongoCollection<ProductDocument> _collection;
    private readonly IProductSearchIndexService _searchIndexService;
    private readonly ILogger<ProductMediaChangedEventConsumer> _logger;
    private readonly ApplicationDbContext? _dbContext;

    public ProductMediaChangedEventConsumer(
        IMongoDatabase database,
        IProductSearchIndexService searchIndexService,
        ILogger<ProductMediaChangedEventConsumer> logger,
        ApplicationDbContext? dbContext = null)
    {
        _collection = database.GetCollection<ProductDocument>("products");
        _searchIndexService = searchIndexService;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductMediaChangedEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductMediaChangedEvent rejected: TenantId is missing | EventId: {EventId} | ProductId: {ProductId}",
                @event.EventId,
                @event.ProductId);
            return;
        }

        ProjectionEventFence? fence = null;
        ProjectionEventFenceKey? fenceKey = null;
        try
        {
            if (_dbContext != null)
            {
                var eventType = typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent);
                fenceKey = new ProjectionEventFenceKey(ConsumerName, eventType, @event.ProductId);
                fence = await ProjectionEventFence.TryAcquireAsync(
                    _dbContext,
                    @event,
                    fenceKey,
                    _logger,
                    context.CancellationToken);

                if (fence == null)
                {
                    _logger.LogInformation(
                        "ProductMediaChangedEvent duplicate or active replay collapsed before side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.ProductId,
                        @event.TenantId);
                    return;
                }

                var sqlAuthority = await LoadSqlMediaAuthorityAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.EventVersion,
                    context.CancellationToken);

                if (sqlAuthority == null)
                {
                    _logger.LogWarning(
                        "ProductMediaChangedEvent rejected by SQL media authority | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
                        @event.EventId,
                        @event.ProductId,
                        @event.TenantId,
                        @event.EventVersion);

                    await fence.CompleteAsync(@event, fenceKey!, context.CancellationToken);
                    return;
                }

                @event.Media = sqlAuthority.Media;
            }

            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, @event.ProductId),
                BuildFreshMediaEventFilter(@event.EventId, @event.OccurredAtUtc, @event.EventVersion));

            var media = (@event.Media ?? new List<ProductMediaItemData>())
                .OrderBy(m => m.Position)
                .Select(m => new ProductMediaDocument
                {
                    Id = m.Id,
                    Type = m.MediaType,
                    Url = m.Url,
                    Provider = m.Provider,
                    Position = m.Position
                })
                .ToList();

            var update = Builders<ProductDocument>.Update
                .Set(p => p.Media, media)
                .Set(p => p.SyncedAtUtc, DateTime.UtcNow)
                .Set(p => p.LastMediaEventId, @event.EventId)
                .Set(p => p.LastMediaEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(p => p.LastMediaEventVersion, @event.EventVersion);

            var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: context.CancellationToken);

            if (result.MatchedCount == 0)
            {
                // Check if product exists and compare LastMediaEventId to determine staleness
                var existingEventId = await _collection.Find(BuildProductFilter(@event.TenantId.Value, @event.ProductId))
                    .Project(p => p.LastMediaEventId)
                    .FirstOrDefaultAsync(context.CancellationToken);

                if (existingEventId != null && existingEventId != Guid.Empty)
                {
                    // Product exists with a LastMediaEventId
                    if (existingEventId == @event.EventId)
                    {
                        _logger.LogInformation(
                            "ProductMediaChangedEvent duplicate retry collapsed with no side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                            @event.EventId,
                            @event.ProductId,
                            @event.TenantId);

                        if (fence != null && _dbContext != null)
                        {
                            await fence.CompleteAsync(@event, fenceKey!, context.CancellationToken);
                        }

                        return;
                    }
                    else
                    {
                        // Different EventId - projection has newer media, skip
                        _logger.LogInformation(
                            "ProductMediaChangedEvent ignored because projection already has newer media | EventId: {EventId} | ExistingEventId: {ExistingEventId} | ProductId: {ProductId} | TenantId: {TenantId} | EventUtc: {EventUtc} | EventVersion: {EventVersion}",
                            @event.EventId,
                            existingEventId,
                            @event.ProductId,
                            @event.TenantId,
                            @event.OccurredAtUtc,
                            @event.EventVersion);
                        if (fence != null && _dbContext != null)
                        {
                            await fence.CompleteAsync(@event, fenceKey!, context.CancellationToken);
                        }

                        return;
                    }
                }

                // Product doesn't exist or has no LastMediaEventId - retry until product projection exists
                _logger.LogWarning(
                    "Product document not found for ProductMediaChangedEvent; retrying until product projection exists | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId,
                    @event.TenantId);
                throw new InvalidOperationException(
                    $"Product read model {@event.ProductId} not found before ProductMediaChangedEvent projection.");
            }

            _logger.LogInformation(
                "Projected product media to MongoDB | ProductId: {ProductId} | TenantId: {TenantId} | MediaCount: {MediaCount}",
                @event.ProductId,
                @event.TenantId,
                media.Count);

            var product = await _collection.Find(BuildProductFilter(@event.TenantId.Value, @event.ProductId))
                .FirstOrDefaultAsync(context.CancellationToken);
            if (product != null)
            {
                var skipSearchIndex = false;
                if (_dbContext != null)
                {
                    product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                        _dbContext,
                        product,
                        @event.TenantId.Value,
                        context.CancellationToken);

                    if (product == null || !product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)
                    {
                        _logger.LogWarning(
                            "ProductMediaChangedEvent search sync deleting stale search document because SQL product authority is unavailable or not searchable | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                            @event.EventId,
                            @event.ProductId,
                            @event.TenantId);
                        var deleted = await _searchIndexService.DeleteAsync(
                            @event.TenantId.Value,
                            @event.ProductId,
                            context.CancellationToken);
                        if (!deleted)
                        {
                            throw new InvalidOperationException(
                                $"Elasticsearch delete failed for ProductMediaChangedEvent product {@event.ProductId}.");
                        }

                        skipSearchIndex = true;
                    }
                }

                if (product != null && !skipSearchIndex)
                {
                    var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(product, @event.EventId);
                    var indexed = await _searchIndexService.IndexAsync(searchDocument, context.CancellationToken);
                    if (!indexed)
                    {
                        throw new InvalidOperationException(
                            $"Elasticsearch index failed for ProductMediaChangedEvent product {@event.ProductId}.");
                    }

                    _logger.LogInformation(
                        "Synced product media to Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId} | PrimaryImage: {PrimaryImage}",
                        @event.ProductId,
                        @event.TenantId,
                        searchDocument.ImageUrl ?? "none");
                }
            }

            if (fence != null && _dbContext != null)
            {
                await fence.CompleteAsync(@event, fenceKey!, context.CancellationToken);
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(
                ex,
                "MongoDB error processing ProductMediaChangedEvent | EventId: {EventId} | ProductId: {ProductId}",
                @event.EventId,
                @event.ProductId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task<SqlMediaAuthoritySnapshot?> LoadSqlMediaAuthorityAsync(
        Guid tenantId,
        Guid productId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return null;
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Id == productId && !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.ProjectionMutationVersion
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return null;
        }

        var sqlVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
        var versionValidation = ProjectionAuthorityValidator.ValidateSqlVersion(
            sqlVersion,
            eventVersion,
            requireExactVersion: true);
        if (!versionValidation.IsAccepted)
        {
            return null;
        }

        var media = await _dbContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ProductId == productId &&
                IsStorefrontProductMediaType(item.MediaType))
            .OrderBy(item => item.Position)
            .ThenBy(item => item.CreatedAtUtc)
            .Select(item => new ProductMediaItemData
            {
                Id = item.Id,
                MediaType = item.MediaType,
                Url = item.Url,
                Provider = item.Provider,
                Position = item.Position
            })
            .ToListAsync(cancellationToken);

        return new SqlMediaAuthoritySnapshot(media);
    }

    private static FilterDefinition<ProductDocument> BuildProductFilter(Guid tenantId, Guid productId)
    {
        return Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId));
    }

    private static FilterDefinition<ProductDocument> BuildFreshMediaEventFilter(
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<ProductDocument>.Filter;
        return filter.And(
            // Reject same event (idempotency)
            filter.Ne(p => p.LastMediaEventId, eventId),
            // Accept if: version is strictly greater OR (version equals AND timestamp is greater)
            // OR LastMediaEventVersion is missing/null (legacy documents)
            // This enforces monotonic ordering by version first, timestamp second
            filter.Or(
                // Legacy documents without LastMediaEventVersion (treat as older)
                filter.Exists(p => p.LastMediaEventVersion, false),
                // Event version is strictly greater (primary ordering)
                filter.Lt(p => p.LastMediaEventVersion, eventVersion),
                // Event version equals, use timestamp as tie-breaker (secondary ordering)
                filter.And(
                    filter.Eq(p => p.LastMediaEventVersion, eventVersion),
                    filter.Lt(p => p.LastMediaEventOccurredAtUtc, occurredAtUtc))));
    }

    private sealed record SqlMediaAuthoritySnapshot(List<ProductMediaItemData> Media);

    private static bool IsStorefrontProductMediaType(string? mediaType) =>
        string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);
}
