using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.ReadModels.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class HomepageProjectionConsumer : IConsumer<HomepageSectionsPublishedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceHomepageReadModelProjectionService _projectionService;
    private readonly ILogger<HomepageProjectionConsumer> _logger;

    public HomepageProjectionConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceHomepageReadModelProjectionService projectionService,
        ILogger<HomepageProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _projectionService = projectionService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<HomepageSectionsPublishedEvent> context)
    {
        var message = context.Message;
        const string consumerName = nameof(HomepageProjectionConsumer);
        const string eventType = nameof(HomepageSectionsPublishedEvent);
        var correlationId = message.CorrelationId ?? context.CorrelationId?.ToString() ?? message.EventId.ToString();
        var entityId = message.TenantId ?? Guid.Empty;

        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            message,
            consumerName,
            eventType,
            entityId,
            context.CancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Homepage projection duplicate or active replay collapsed before side effects | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                message.EventId,
                message.TenantId,
                correlationId);
            return;
        }

        var occurredAtUtc = message.PublishedAtUtc == default
            ? message.OccurredAtUtc
            : message.PublishedAtUtc;

        var projectionResult = await _projectionService.RebuildAsync(
            message.TenantId,
            message.EventId,
            occurredAtUtc,
            message.EventVersion,
            context.CancellationToken);

        await fence.CompleteAsync(
            message,
            consumerName,
            eventType,
            entityId,
            context.CancellationToken);

        _logger.LogInformation(
            "Homepage projection processed | Result: {Result} | EventId: {EventId} | TenantId: {TenantId} | EventVersion: {EventVersion}",
            projectionResult,
            message.EventId,
            message.TenantId,
            message.EventVersion);
    }
}

public sealed class HomepageProjectionConsumerDefinition : ConsumerDefinition<HomepageProjectionConsumer>
{
    public HomepageProjectionConsumerDefinition()
    {
        EndpointName = "homepage-readmodel-projection";
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<HomepageProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
