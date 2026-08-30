using MassTransit;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Definition for OrderProjectionConsumer.
/// Explicitly sets endpoint name to force topology update (versioning).
/// </summary>
public class OrderProjectionConsumerDefinition : ConsumerDefinition<OrderProjectionConsumer>
{
    public OrderProjectionConsumerDefinition()
    {
        // v7: project authoritative fulfillment lifecycle visibility, including
        // returned and failed-delivery terminal states.
        EndpointName = "order-projection-v7";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<OrderProjectionConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Default retry policy
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
    }
}
