using MassTransit;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer definition for ProviderTrustConsumer.
/// PHASE: Marketplace Audit Hardening (P1-4)
/// 
/// Configures retry policy and endpoint name for the trust event processor.
/// Uses exponential backoff with 5 retries to handle transient failures
/// (database timeouts, concurrency conflicts that exceed internal retries).
/// </summary>
public class ProviderTrustConsumerDefinition : ConsumerDefinition<ProviderTrustConsumer>
{
    public ProviderTrustConsumerDefinition()
    {
        EndpointName = "marketplace-trust-v1";
        ConcurrentMessageLimit = 5; // Limit concurrent trust updates to reduce contention
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProviderTrustConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Exponential backoff: 2s, 4s, 8s, 16s, 32s
        endpointConfigurator.UseMessageRetry(r => r
            .Exponential(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(2)));
    }
}
