namespace PointOfSale.Core.Events;

public abstract class CommerceHubPaymentCompletionEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid MarketplaceOrderId { get; set; }
    public Guid CommerceHubPaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderIntentId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public List<Guid> AffectedStoreIds { get; set; } = new();

    public Guid GetAuthoritativeAggregateId() => MarketplaceOrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() => AffectedStoreIds;
}

public sealed class CommerceHubPaymentCompletedEvent : CommerceHubPaymentCompletionEvent;

public sealed class CommerceHubOrderPaidEvent : CommerceHubPaymentCompletionEvent;

public sealed class CommerceHubVendorEarningsCreatedEvent : CommerceHubPaymentCompletionEvent
{
    public int EarningCount { get; set; }
}

public sealed class CommerceHubSettlementCreatedEvent : CommerceHubPaymentCompletionEvent
{
    public int SettlementEntryCount { get; set; }
}

public sealed class CommerceHubInventoryFinalizedEvent : CommerceHubPaymentCompletionEvent
{
    public int ReservationCount { get; set; }
}

public sealed class CommerceHubPaymentNotificationsRequestedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    public Guid MarketplaceOrderId { get; set; }
    public Guid CommerceHubPaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
}
