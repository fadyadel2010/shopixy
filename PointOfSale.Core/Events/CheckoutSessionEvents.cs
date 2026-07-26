namespace PointOfSale.Core.Events;

/// <summary>
/// Published when a customer enters the checkout page and a session is created.
/// Consumer projects this to MongoDB for the admin dashboard.
/// </summary>
public class CheckoutSessionStartedEvent : IntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
    public Guid CartId { get; set; }
    public string? SessionId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string ItemsSnapshotJson { get; set; } = "[]";
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
    public string RecoveryToken { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Published when customer updates checkout fields (progressive autosave).
/// Consumer updates the MongoDB projection with latest data.
/// 
/// SECURITY NOTE: Sensitive payment proof fields (ProofImageUrl, WalletType, WalletPhoneNumber)
/// are NOT included in this public event. They are published separately via
/// CheckoutSessionProofEvent for the projection consumer only.
/// </summary>
public class CheckoutSessionUpdatedEvent : IntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }
    public string? ItemsSnapshotJson { get; set; }
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? PaymentMethodCode { get; set; }
    public DateTime? FirstActivityAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
}

/// <summary>
/// Internal-scoped integration event for payment proof data.
/// 
/// CRITICAL: This event bypasses the outbox and is published directly to the message bus
/// to prevent persisting sensitive data (WalletPhoneNumber, ProofImageUrl) in the outbox tables.
/// Published via IEventPublisher.PublishSensitiveAsync(), which uses IBus directly instead of
/// the scoped publish endpoint so sensitive payloads do not enter the MassTransit bus outbox.
/// A durable CheckoutSessionProofSyncRequestedEvent is also staged through the outbox with only
/// identifiers so projections can repair themselves if this best-effort fast path is lost.
/// 
/// SECURITY: Contains sensitive payment proof fields (WalletType, WalletPhoneNumber, ProofImageUrl)
/// that are separated from the public CheckoutSessionUpdatedEvent to minimize exposure.
/// Consumers other than the projection service should NOT subscribe to this event.
/// </summary>
public class CheckoutSessionProofEvent : IntegrationEvent, ISensitiveIntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
    public string? WalletType { get; set; }
    public string? WalletPhoneNumber { get; set; }
    public string? ProofImageUrl { get; set; }
}

/// <summary>
/// Durable, non-sensitive request for the projection consumer to refresh manual wallet proof fields.
/// Carries only identifiers so it can safely use the transactional outbox without persisting proof URLs
/// or wallet phone numbers in outbox storage.
/// </summary>
public class CheckoutSessionProofSyncRequestedEvent : IntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
}

/// <summary>
/// Published when background scanner detects an idle checkout session.
/// Consumer updates MongoDB status to Abandoned.
/// </summary>
public class CheckoutSessionAbandonedEvent : IntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
    public Guid CartId { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? ItemsSnapshotJson { get; set; }
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
    public string? DeviceType { get; set; }
    public string? Source { get; set; }
    public string RecoveryToken { get; set; } = string.Empty;
    public DateTime? FirstActivityAtUtc { get; set; }
    public DateTime AbandonedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
}

/// <summary>
/// Published when checkout completes and an order is created.
/// Consumer updates MongoDB status to ConvertedToOrder.
/// </summary>
public class CheckoutSessionConvertedEvent : IntegrationEvent
{
    public Guid CheckoutSessionId { get; set; }
    public Guid CartId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime ConvertedAtUtc { get; set; }
}
