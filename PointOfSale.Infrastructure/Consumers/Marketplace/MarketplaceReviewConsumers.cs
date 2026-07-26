using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Services.MarketplaceReviews;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class MarketplaceReviewCreatedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ReviewCreatedEvent>
{
    private readonly IMarketplaceProductReviewModerationService _moderationService;

    public MarketplaceReviewCreatedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceProductReviewModerationService moderationService,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceReviewCreatedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _moderationService = moderationService;
    }

    protected override string ConsumerName => nameof(MarketplaceReviewCreatedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.ReviewCreatedEvent> context, CancellationToken cancellationToken)
        => _moderationService.HandleCreatedAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceReviewUpdatedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ReviewUpdatedEvent>
{
    private readonly IMarketplaceProductReviewModerationService _moderationService;
    private readonly IMarketplaceProductReviewProjectionService _projectionService;

    public MarketplaceReviewUpdatedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceProductReviewModerationService moderationService,
        IMarketplaceProductReviewProjectionService projectionService,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceReviewUpdatedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _moderationService = moderationService;
        _projectionService = projectionService;
    }

    protected override string ConsumerName => nameof(MarketplaceReviewUpdatedConsumer);

    protected override async Task HandleAsync(ConsumeContext<MarketplaceEvents.ReviewUpdatedEvent> context, CancellationToken cancellationToken)
    {
        await _projectionService.HandleUpdatedAsync(context.Message, cancellationToken);
        await _moderationService.HandleUpdatedAsync(context.Message, cancellationToken);
    }
}

public sealed class MarketplaceReviewApprovedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ReviewApprovedEvent>
{
    private readonly IMarketplaceProductReviewProjectionService _projectionService;

    public MarketplaceReviewApprovedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceProductReviewProjectionService projectionService,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceReviewApprovedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _projectionService = projectionService;
    }

    protected override string ConsumerName => nameof(MarketplaceReviewApprovedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.ReviewApprovedEvent> context, CancellationToken cancellationToken)
        => _projectionService.HandleApprovedAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceReviewRejectedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ReviewRejectedEvent>
{
    private readonly IMarketplaceProductReviewProjectionService _projectionService;

    public MarketplaceReviewRejectedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceProductReviewProjectionService projectionService,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceReviewRejectedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _projectionService = projectionService;
    }

    protected override string ConsumerName => nameof(MarketplaceReviewRejectedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.ReviewRejectedEvent> context, CancellationToken cancellationToken)
        => _projectionService.HandleRejectedAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceReviewDeletedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ReviewDeletedEvent>
{
    private readonly IMarketplaceProductReviewProjectionService _projectionService;

    public MarketplaceReviewDeletedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceProductReviewProjectionService projectionService,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceReviewDeletedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _projectionService = projectionService;
    }

    protected override string ConsumerName => nameof(MarketplaceReviewDeletedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.ReviewDeletedEvent> context, CancellationToken cancellationToken)
        => _projectionService.HandleDeletedAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceReviewCreatedConsumerDefinition : ConsumerDefinition<MarketplaceReviewCreatedConsumer>
{
    public MarketplaceReviewCreatedConsumerDefinition()
    {
        EndpointName = "marketplace-review-created-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceReviewCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}

public sealed class MarketplaceReviewUpdatedConsumerDefinition : ConsumerDefinition<MarketplaceReviewUpdatedConsumer>
{
    public MarketplaceReviewUpdatedConsumerDefinition()
    {
        EndpointName = "marketplace-review-updated-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceReviewUpdatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}

public sealed class MarketplaceReviewApprovedConsumerDefinition : ConsumerDefinition<MarketplaceReviewApprovedConsumer>
{
    public MarketplaceReviewApprovedConsumerDefinition()
    {
        EndpointName = "marketplace-review-approved-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceReviewApprovedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}

public sealed class MarketplaceReviewRejectedConsumerDefinition : ConsumerDefinition<MarketplaceReviewRejectedConsumer>
{
    public MarketplaceReviewRejectedConsumerDefinition()
    {
        EndpointName = "marketplace-review-rejected-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceReviewRejectedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}

public sealed class MarketplaceReviewDeletedConsumerDefinition : ConsumerDefinition<MarketplaceReviewDeletedConsumer>
{
    public MarketplaceReviewDeletedConsumerDefinition()
    {
        EndpointName = "marketplace-review-deleted-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceReviewDeletedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}
