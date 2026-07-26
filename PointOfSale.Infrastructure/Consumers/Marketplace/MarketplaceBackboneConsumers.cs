using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Eventing;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Infrastructure.Consumers.Marketplace;

public sealed class MarketplaceOrderCreatedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.OrderCreatedEvent>
{
    private readonly IMarketplaceEventPipeline _pipeline;

    public MarketplaceOrderCreatedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceEventPipeline pipeline,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceOrderCreatedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _pipeline = pipeline;
    }

    protected override string ConsumerName => nameof(MarketplaceOrderCreatedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.OrderCreatedEvent> context, CancellationToken cancellationToken)
        => _pipeline.ProcessAsync(context.Message, cancellationToken);
}

public sealed class MarketplacePaymentCompletedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.PaymentCompletedEvent>
{
    private readonly IMarketplaceEventPipeline _pipeline;

    public MarketplacePaymentCompletedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceEventPipeline pipeline,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplacePaymentCompletedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _pipeline = pipeline;
    }

    protected override string ConsumerName => nameof(MarketplacePaymentCompletedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.PaymentCompletedEvent> context, CancellationToken cancellationToken)
        => _pipeline.ProcessAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceOrderDeliveredConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.OrderDeliveredEvent>
{
    private readonly IMarketplaceEventPipeline _pipeline;

    public MarketplaceOrderDeliveredConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceEventPipeline pipeline,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceOrderDeliveredConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _pipeline = pipeline;
    }

    protected override string ConsumerName => nameof(MarketplaceOrderDeliveredConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.OrderDeliveredEvent> context, CancellationToken cancellationToken)
        => _pipeline.ProcessAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceRefundProcessedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.RefundProcessedEvent>
{
    private readonly IMarketplaceEventPipeline _pipeline;

    public MarketplaceRefundProcessedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceEventPipeline pipeline,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceRefundProcessedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _pipeline = pipeline;
    }

    protected override string ConsumerName => nameof(MarketplaceRefundProcessedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.RefundProcessedEvent> context, CancellationToken cancellationToken)
        => _pipeline.ProcessAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceProductUpdatedConsumer : MarketplaceIdempotentConsumer<MarketplaceEvents.ProductUpdatedEvent>
{
    private readonly IMarketplaceEventPipeline _pipeline;

    public MarketplaceProductUpdatedConsumer(
        ApplicationDbContext dbContext,
        IMarketplaceEventPipeline pipeline,
        IEventLagTracker lagTracker,
        MarketplaceProjectionOutboxProcessor projectionOutboxProcessor,
        ILogger<MarketplaceProductUpdatedConsumer> logger)
        : base(dbContext, logger, lagTracker, projectionOutboxProcessor)
    {
        _pipeline = pipeline;
    }

    protected override string ConsumerName => nameof(MarketplaceProductUpdatedConsumer);

    protected override Task HandleAsync(ConsumeContext<MarketplaceEvents.ProductUpdatedEvent> context, CancellationToken cancellationToken)
        => _pipeline.ProcessAsync(context.Message, cancellationToken);
}

public sealed class MarketplaceOrderCreatedConsumerDefinition : ConsumerDefinition<MarketplaceOrderCreatedConsumer>
{
    public MarketplaceOrderCreatedConsumerDefinition()
    {
        EndpointName = "order-created-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceOrderCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 10;
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ResetInterval = TimeSpan.FromMinutes(2);
        });
    }
}

public sealed class MarketplacePaymentCompletedConsumerDefinition : ConsumerDefinition<MarketplacePaymentCompletedConsumer>
{
    public MarketplacePaymentCompletedConsumerDefinition()
    {
        EndpointName = "payment-completed-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplacePaymentCompletedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 10;
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ResetInterval = TimeSpan.FromMinutes(2);
        });
    }
}

public sealed class MarketplaceOrderDeliveredConsumerDefinition : ConsumerDefinition<MarketplaceOrderDeliveredConsumer>
{
    public MarketplaceOrderDeliveredConsumerDefinition()
    {
        EndpointName = "order-delivered-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceOrderDeliveredConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 10;
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ResetInterval = TimeSpan.FromMinutes(2);
        });
    }
}

public sealed class MarketplaceRefundProcessedConsumerDefinition : ConsumerDefinition<MarketplaceRefundProcessedConsumer>
{
    public MarketplaceRefundProcessedConsumerDefinition()
    {
        EndpointName = "refund-processed-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceRefundProcessedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 10;
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ResetInterval = TimeSpan.FromMinutes(2);
        });
    }
}

public sealed class MarketplaceProductUpdatedConsumerDefinition : ConsumerDefinition<MarketplaceProductUpdatedConsumer>
{
    public MarketplaceProductUpdatedConsumerDefinition()
    {
        EndpointName = "product-updated-queue";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<MarketplaceProductUpdatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseCircuitBreaker(cb =>
        {
            cb.ActiveThreshold = 10;
            cb.TrackingPeriod = TimeSpan.FromMinutes(1);
            cb.TripThreshold = 15;
            cb.ResetInterval = TimeSpan.FromMinutes(2);
        });
    }
}
