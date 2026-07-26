using MassTransit;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer definition for ProviderTrustProjectionConsumer.
/// PHASE: Marketplace Audit Hardening (P1-4)
/// 
/// Configures retry policy for the MongoDB trust projection updater.
/// Uses interval retry since MongoDB upserts are idempotent — retries are safe.
/// </summary>
public class ProviderTrustProjectionConsumerDefinition : ConsumerDefinition<ProviderTrustProjectionConsumer>
{
    public ProviderTrustProjectionConsumerDefinition()
    {
        EndpointName = "marketplace-trust-projection-v1";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProviderTrustProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Interval retry: 3 attempts, 5s apart (MongoDB upserts are idempotent)
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
    }
}
