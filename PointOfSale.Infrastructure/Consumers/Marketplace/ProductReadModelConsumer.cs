using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class ProductReadModelConsumer
{
    private readonly IMarketplaceProductReadModelProjectionService _projectionService;
    private readonly ILogger<ProductReadModelConsumer> _logger;

    public ProductReadModelConsumer(
        IMarketplaceProductReadModelProjectionService projectionService,
        ILogger<ProductReadModelConsumer> logger)
    {
        _projectionService = projectionService;
        _logger = logger;
    }

    public async Task ConsumeAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        var result = await _projectionService.RebuildAsync(
            @event.EntityId,
            @event.TenantId,
            @event.EventId,
            @event.OccurredAtUtc,
            @event.EventVersion,
            cancellationToken);

        switch (result)
        {
            case MarketplaceProjectionResult.Applied:
                _logger.LogInformation(
                    "Marketplace product read model projected | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId,
                    @event.EventVersion);
                break;

            case MarketplaceProjectionResult.SkippedStale:
                _logger.LogInformation(
                    "Marketplace product read model skipped stale event | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId,
                    @event.EventVersion);
                break;

            case MarketplaceProjectionResult.Deleted:
                _logger.LogInformation(
                    "Marketplace product read model removed because SQL source no longer exists | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId);
                break;

            default:
                _logger.LogWarning(
                    "Marketplace product read model source product not found | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId}",
                    @event.EntityId,
                    @event.TenantId,
                    @event.EventId);
                break;
        }
    }
}
