using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Consumers;

public class CommerceHubOrderProjectionConsumer :
    IConsumer<CommerceHubMarketplaceOrderCreatedEvent>,
    IConsumer<CommerceHubMarketplaceOrderStatusUpdatedEvent>,
    IConsumer<CommerceHubSubOrderStatusUpdatedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<CommerceHubOrderDocument> _collection;
    private readonly ILogger<CommerceHubOrderProjectionConsumer> _logger;

    public CommerceHubOrderProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<CommerceHubOrderProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<CommerceHubOrderDocument>("commercehub_orders");
        _logger = logger;
        EnsureIndexes();
    }

    public Task Consume(ConsumeContext<CommerceHubMarketplaceOrderCreatedEvent> context) =>
        ProjectAsync(context.Message.MarketplaceOrderId, context.Message.EventId, context.Message.OccurredAtUtc, context.Message.EventVersion, context.CancellationToken);

    public Task Consume(ConsumeContext<CommerceHubMarketplaceOrderStatusUpdatedEvent> context) =>
        ProjectAsync(context.Message.MarketplaceOrderId, context.Message.EventId, context.Message.OccurredAtUtc, context.Message.EventVersion, context.CancellationToken);

    public Task Consume(ConsumeContext<CommerceHubSubOrderStatusUpdatedEvent> context) =>
        ProjectAsync(context.Message.MarketplaceOrderId, context.Message.EventId, context.Message.OccurredAtUtc, context.Message.EventVersion, context.CancellationToken);

    private async Task ProjectAsync(
        Guid marketplaceOrderId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Include(x => x.SubOrders)
            .ThenInclude(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == marketplaceOrderId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning(
                "CommerceHub order projection skipped because order was not found | MarketplaceOrderId: {MarketplaceOrderId}",
                marketplaceOrderId);
            return;
        }

        // Apply ordering client-side after materialization
        var orderedSubOrders = order.SubOrders.GetOrderedSubOrders().ToList();
        order.SubOrders = orderedSubOrders;

        if (incomingEventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"CommerceHub order event {eventId} has invalid EventVersion {incomingEventVersion}. EventVersion must be positive.");
        }

        var sourceMutationVersion = ResolveSourceMutationVersion(order);

        var document = new CommerceHubOrderDocument
        {
            MarketplaceOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            FullName = order.FullName,
            PhoneNumber = order.PhoneNumber,
            AddressLine = order.AddressLine,
            City = order.City,
            Area = order.Area,
            Notes = order.Notes,
            Currency = order.Currency,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PaymentReference = order.PaymentReference,
            PaymentReferenceId = order.PaymentReferenceId,
            PaymentGateway = order.PaymentGateway,
            Subtotal = order.Subtotal,
            ShippingTotal = order.ShippingTotal,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt ?? order.CreatedAt,
            PaidAt = order.PaidAt,
            LastEventId = eventId,
            LastEventOccurredAtUtc = incomingOccurredAtUtc,
            LastEventVersion = incomingEventVersion,
            LastSourceMutationTicks = sourceMutationVersion,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            SubOrders = order.SubOrders.Select(subOrder => new CommerceHubSubOrderDocument
            {
                SubOrderId = subOrder.Id,
                OrderNumber = subOrder.OrderNumber,
                StoreId = subOrder.StoreId,
                StoreName = subOrder.StoreName,
                Currency = subOrder.Currency,
                Subtotal = subOrder.Subtotal,
                Shipping = subOrder.Shipping,
                ShippingMethodId = subOrder.ShippingMethodId,
                ShippingMethodName = subOrder.ShippingMethodName,
                ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                PaymentMethod = subOrder.PaymentMethod,
                PaymentMethodCode = subOrder.PaymentMethodCode,
                PaymentMethodName = subOrder.PaymentMethodName,
                PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                Total = subOrder.Total,
                Status = subOrder.Status,
                PaymentStatus = subOrder.PaymentStatus,
                PaymentReferenceId = subOrder.PaymentReferenceId,
                PaidAt = subOrder.PaidAt,
                Items = subOrder.Items.Select(item => new CommerceHubOrderItemDocument
                {
                    ListingId = item.ListingId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    VariantAttributesJson = item.VariantAttributesJson,
                    ImageUrl = item.ImageUrl,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total
                }).ToList()
            }).ToList()
        };
        document.ProjectionChecksum = CommerceHubOrderProjectionChecksum.Compute(document);

        // Legacy documents may have DateTime ticks (> 600000000000000000) in LastSourceMutationTicks.
        // Treat any value >= LegacyTickThreshold as legacy and always allow replacement.
        const long LegacyTickThreshold = 600000000000000000L; // Year ~1900 in ticks
        
        var staleSafeFilter = Builders<CommerceHubOrderDocument>.Filter.And(
            Builders<CommerceHubOrderDocument>.Filter.Eq(x => x.MarketplaceOrderId, marketplaceOrderId),
            Builders<CommerceHubOrderDocument>.Filter.Or(
                Builders<CommerceHubOrderDocument>.Filter.Exists(nameof(CommerceHubOrderDocument.LastEventOccurredAtUtc), false),
                Builders<CommerceHubOrderDocument>.Filter.Gte(x => x.LastSourceMutationTicks, LegacyTickThreshold),
                Builders<CommerceHubOrderDocument>.Filter.Lt(x => x.LastSourceMutationTicks, sourceMutationVersion),
                Builders<CommerceHubOrderDocument>.Filter.And(
                    Builders<CommerceHubOrderDocument>.Filter.Eq(x => x.LastSourceMutationTicks, sourceMutationVersion),
                    Builders<CommerceHubOrderDocument>.Filter.Or(
                        Builders<CommerceHubOrderDocument>.Filter.Exists(nameof(CommerceHubOrderDocument.ProjectionChecksum), false),
                        Builders<CommerceHubOrderDocument>.Filter.Ne(x => x.ProjectionChecksum, document.ProjectionChecksum),
                        Builders<CommerceHubOrderDocument>.Filter.Eq(x => x.LastEventId, eventId),
                        Builders<CommerceHubOrderDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                        Builders<CommerceHubOrderDocument>.Filter.And(
                            Builders<CommerceHubOrderDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, incomingOccurredAtUtc),
                            Builders<CommerceHubOrderDocument>.Filter.Lt(x => x.LastEventVersion, incomingEventVersion))))));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                staleSafeFilter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            if (result.MatchedCount == 0 && result.UpsertedId == null)
            {
                _logger.LogInformation(
                    "CommerceHub order projection rejected stale event | MarketplaceOrderId: {MarketplaceOrderId} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion}",
                    marketplaceOrderId,
                    eventId,
                    incomingOccurredAtUtc,
                    incomingEventVersion);
                return;
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation(
                "CommerceHub order projection collapsed stale duplicate-key race | MarketplaceOrderId: {MarketplaceOrderId} | EventId: {EventId}",
                marketplaceOrderId,
                eventId);
            return;
        }

        if (!order.IsProjected)
        {
            await MarkOrderAsProjectedAsync(order.Id, cancellationToken);
        }
    }

    private static long ResolveSourceMutationVersion(CommerceHubMarketplaceOrder order)
    {
        var sourceVersion = order.ProjectionMutationVersion;

        foreach (var subOrder in order.SubOrders)
        {
            sourceVersion = Math.Max(sourceVersion, subOrder.ProjectionMutationVersion);
        }

        return sourceVersion <= 0 ? 1L : sourceVersion;
    }

    private async Task MarkOrderAsProjectedAsync(Guid marketplaceOrderId, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _dbContext.CommerceHubMarketplaceOrders
                .FirstOrDefaultAsync(x => x.Id == marketplaceOrderId, cancellationToken);

            if (entity != null && !entity.IsProjected)
            {
                using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginProjection();
                entity.IsProjected = true;
                entity.ProjectedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub order projection tracking update failed | MarketplaceOrderId: {MarketplaceOrderId}",
                marketplaceOrderId);
        }
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<CommerceHubOrderDocument>.IndexKeys;
            _collection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<CommerceHubOrderDocument>(
                    indexKeys.Ascending(x => x.MarketplaceOrderId),
                    new CreateIndexOptions { Unique = true, Name = "IX_MarketplaceOrderId" }),
                new CreateIndexModel<CommerceHubOrderDocument>(
                    indexKeys.Ascending(x => x.OrderNumber),
                    new CreateIndexOptions { Unique = true, Name = "IX_OrderNumber" }),
                new CreateIndexModel<CommerceHubOrderDocument>(
                    indexKeys.Descending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "IX_CreatedAt" }),
                new CreateIndexModel<CommerceHubOrderDocument>(
                    indexKeys.Ascending(x => x.MarketplaceOrderId)
                        .Descending(x => x.LastEventOccurredAtUtc)
                        .Descending(x => x.LastEventVersion),
                    new CreateIndexOptions { Name = "IX_MarketplaceOrder_EventOrdering" })
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure MongoDB indexes for CommerceHub orders projection");
        }
    }
}

public class CommerceHubOrderProjectionConsumerDefinition : ConsumerDefinition<CommerceHubOrderProjectionConsumer>
{
    public CommerceHubOrderProjectionConsumerDefinition()
    {
        EndpointName = "commercehub-order-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CommerceHubOrderProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
