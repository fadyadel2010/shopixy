using MassTransit;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer definition for ServiceDiscoveryProjectionConsumer.
/// PHASE: Marketplace Audit Hardening (P1-4)
/// 
/// Configures retry policy for the Elasticsearch projection updater.
/// Uses exponential backoff since ES failures are often transient (cluster overload, 
/// node restarts). Higher retry count gives ES time to recover.
/// </summary>
public class ServiceDiscoveryProjectionConsumerDefinition : ConsumerDefinition<ServiceDiscoveryProjectionConsumer>
{
    public ServiceDiscoveryProjectionConsumerDefinition()
    {
        EndpointName = "marketplace-discovery-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ServiceDiscoveryProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Exponential backoff: 3s, 6s, 12s, 24s, 48s
        // ES failures are often transient — give the cluster time to recover
        endpointConfigurator.UseMessageRetry(r => r
            .Exponential(5, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(3)));
    }
}
