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
        // v6: AF-35 hardening — add affiliate commission reversal binding and
        // rebuild missing affiliate storefront projections from SQL authority.
        EndpointName = "order-projection-v6";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<OrderProjectionConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Default retry policy
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
    }
}
