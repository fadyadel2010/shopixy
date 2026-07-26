using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Tests for the authoritative event type marker interface.
/// 
/// CRITICAL: Authoritative events MUST implement IAuthoritativeIntegrationEvent
/// to ensure they are published within a transaction boundary.
/// </summary>
public sealed class AuthoritativeEventTypeTests
{
    [Theory]
    [InlineData(typeof(OrderCreatedEvent))]
    [InlineData(typeof(OrderStatusChangedEvent))]
    [InlineData(typeof(OrderPaymentStatusChangedEvent))]
    [InlineData(typeof(OrderShippedEvent))]
    [InlineData(typeof(OrderDeliveredEvent))]
    [InlineData(typeof(OrderCancelledEvent))]
    public void OrderEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(PaymentIntentCreatedEvent))]
    [InlineData(typeof(PaymentAuthorizedEvent))]
    [InlineData(typeof(PaymentCapturedEvent))]
    [InlineData(typeof(PaymentFailedEvent))]
    [InlineData(typeof(OrderPaidEvent))]
    [InlineData(typeof(OrderConfirmedEvent))]
    public void PaymentEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(OfflinePaymentCollectedEvent))]
    [InlineData(typeof(OrderRefundedEvent))]
    public void PaymentLifecycleEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(FulfillmentConfirmedEvent))]
    [InlineData(typeof(OrderPackedEvent))]
    [InlineData(typeof(OrderOutForDeliveryEvent))]
    [InlineData(typeof(FulfillmentDeliveredEvent))]
    [InlineData(typeof(OrderCompletedEvent))]
    [InlineData(typeof(OrderReturnedEvent))]
    [InlineData(typeof(OrderDeliveryFailedEvent))]
    [InlineData(typeof(OrderFulfillmentCancelledEvent))]
    public void FulfillmentEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(AffiliateOrderPlacedEvent))]
    [InlineData(typeof(OrderItemRefundedEvent))]
    public void AffiliateEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(CommerceHubMarketplaceOrderCreatedEvent))]
    [InlineData(typeof(CommerceHubMarketplaceOrderStatusUpdatedEvent))]
    [InlineData(typeof(CommerceHubSubOrderStatusUpdatedEvent))]
    public void CommerceHubEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        // Assert
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(StorefrontCmsPagePublishedEvent))]
    [InlineData(typeof(StorefrontCmsPageDeletedEvent))]
    [InlineData(typeof(NavigationPublishedEvent))]
    public void StorefrontProjectionTruthEvents_ShouldBeMarkedAsAuthoritative(Type eventType)
    {
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement IAuthoritativeIntegrationEvent");
    }

    [Theory]
    [InlineData(typeof(MarketplaceCmsPagePublishedEvent))]
    [InlineData(typeof(MarketplaceCmsPageDeletedEvent))]
    public void MarketplaceCmsCacheInvalidationEvents_ShouldRemainNonAuthoritative(Type eventType)
    {
        Assert.False(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must not implement IAuthoritativeIntegrationEvent");
        Assert.True(
            typeof(INonAuthoritativeIntegrationEvent).IsAssignableFrom(eventType),
            $"{eventType.Name} must implement INonAuthoritativeIntegrationEvent");
    }

    [Fact]
    public void MarkerInterface_ShouldBeExplicitOptIn()
    {
        // Arrange: Create a test event that doesn't implement the marker
        var testEvent = new TestNonAuthoritativeEvent();

        // Assert: Should not be considered authoritative
        Assert.False(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(testEvent.GetType()),
            "Events without the marker interface should not be considered authoritative");
    }

    [Fact]
    public void MarkerInterface_ShouldBeInheritedFromBaseClass()
    {
        // Arrange: PaymentEventBase implements IAuthoritativeIntegrationEvent
        var derivedEvent = new PaymentCapturedEvent();

        // Assert: Derived classes should inherit the marker
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(derivedEvent.GetType()),
            "Derived classes should inherit IAuthoritativeIntegrationEvent from base class");
    }

    [Fact]
    public void MarkerInterface_ShouldBeInheritedFromFulfillmentBase()
    {
        // Arrange: OrderFulfillmentEventBase implements IAuthoritativeIntegrationEvent
        var derivedEvent = new FulfillmentConfirmedEvent();

        // Assert: Derived classes should inherit the marker
        Assert.True(
            typeof(IAuthoritativeIntegrationEvent).IsAssignableFrom(derivedEvent.GetType()),
            "Derived classes should inherit IAuthoritativeIntegrationEvent from OrderFulfillmentEventBase");
    }

    /// <summary>
    /// Test event that does NOT implement IAuthoritativeIntegrationEvent
    /// </summary>
    private sealed class TestNonAuthoritativeEvent : IntegrationEvent
    {
    }
}
