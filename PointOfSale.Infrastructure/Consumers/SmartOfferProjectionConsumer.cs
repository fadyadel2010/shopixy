using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Consumers;

public class SmartOfferProjectionConsumer :
    IConsumer<SmartOfferCreatedEvent>,
    IConsumer<SmartOfferUpdatedEvent>,
    IConsumer<SmartOfferDeletedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<StorefrontConfigDocument> _collection;
    private readonly ILogger<SmartOfferProjectionConsumer> _logger;

    public SmartOfferProjectionConsumer(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<SmartOfferProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _collection = mongoDatabase.GetCollection<StorefrontConfigDocument>("storefront_configs");
        _logger = logger;
    }

    public Task Consume(ConsumeContext<SmartOfferCreatedEvent> context)
    {
        return SyncTenantSmartOffersAsync(context.Message.TenantId!.Value, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<SmartOfferUpdatedEvent> context)
    {
        return SyncTenantSmartOffersAsync(context.Message.TenantId!.Value, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<SmartOfferDeletedEvent> context)
    {
        return SyncTenantSmartOffersAsync(context.Message.TenantId!.Value, context.CancellationToken);
    }

    private async Task SyncTenantSmartOffersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var offers = await _dbContext.SmartOffers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Select(MapToDocumentExpression())
            .ToListAsync(cancellationToken);

        var filter = Builders<StorefrontConfigDocument>.Filter.Eq(x => x.TenantId, tenantId);
        var update = Builders<StorefrontConfigDocument>.Update
            .Set(x => x.SmartOffers, offers)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            _logger.LogWarning(
                "StorefrontConfig not found while syncing smart offers | TenantId: {TenantId} | OfferCount: {OfferCount}",
                tenantId,
                offers.Count);
            return;
        }

        _logger.LogInformation(
            "Smart offers projected to storefront config | TenantId: {TenantId} | OfferCount: {OfferCount}",
            tenantId,
            offers.Count);
    }

    private static System.Linq.Expressions.Expression<Func<SmartOffer, StorefrontSmartOfferDocument>> MapToDocumentExpression()
    {
        return offer => new StorefrontSmartOfferDocument
        {
            Id = offer.Id,
            Name = offer.Name,
            Status = offer.Status,
            Priority = offer.Priority,
            TriggerType = offer.TriggerType,
            PagesScope = offer.PagesScope,
            DelaySeconds = offer.DelaySeconds,
            ScrollPercent = offer.ScrollPercent,
            ShowOncePerSession = offer.ShowOncePerSession,
            MaxDisplaysPerUser = offer.MaxDisplaysPerUser,
            PopupTitle = offer.PopupTitle,
            PopupDescription = offer.PopupDescription,
            ButtonText = offer.ButtonText,
            CouponCode = offer.CouponCode,
            ImageUrl = offer.ImageUrl,
            BackgroundColor = offer.BackgroundColor
        };
    }
}

public class SmartOfferProjectionConsumerDefinition : ConsumerDefinition<SmartOfferProjectionConsumer>
{
    public SmartOfferProjectionConsumerDefinition()
    {
        EndpointName = "smart-offer-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SmartOfferProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, 1000));
    }
}
