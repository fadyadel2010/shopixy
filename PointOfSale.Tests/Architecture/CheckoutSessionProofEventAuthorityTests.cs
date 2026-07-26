using PointOfSale.Core.Events;

namespace PointOfSale.Tests.Architecture;

public sealed class CheckoutSessionProofEventAuthorityTests
{
    [Fact]
    public void CheckoutSessionProofEvent_MustBeSensitiveButNonAuthoritative()
    {
        Assert.True(typeof(ISensitiveIntegrationEvent).IsAssignableFrom(typeof(CheckoutSessionProofEvent)));
        Assert.True(typeof(INonAuthoritativeIntegrationEvent).IsAssignableFrom(typeof(CheckoutSessionProofEvent)));
        Assert.False(IntegrationEventAuthority.IsAuthoritativeEventType(typeof(CheckoutSessionProofEvent)));
    }
}
