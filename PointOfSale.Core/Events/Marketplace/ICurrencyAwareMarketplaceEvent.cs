namespace PointOfSale.Core.Events.Marketplace;

/// <summary>
/// Contract for marketplace domain events that carry currency information.
/// Enables compile-time currency validation for financial events.
/// </summary>
public interface ICurrencyAwareMarketplaceEvent : IMarketplaceDomainEvent
{
    /// <summary>
    /// ISO 4217 currency code (e.g., "USD", "EUR").
    /// </summary>
    string Currency { get; }
    
    /// <summary>
    /// Event category for telemetry and validation (e.g., "checkout", "payment").
    /// </summary>
    string EventCategory { get; }
    
    /// <summary>
    /// Event name for telemetry (e.g., "marketplace.order_created.currency").
    /// </summary>
    string EventName { get; }
}
