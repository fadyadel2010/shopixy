namespace PointOfSale.Core.Events;

public interface IAffectedTenantScopedIntegrationEvent
{
    IReadOnlyCollection<Guid> GetAffectedTenantIds();
}

/// <summary>
/// Raised when a new CommerceHub listing is created for a variant.
/// </summary>
public class CommerceHubListingCreatedEvent : IntegrationEvent
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public bool IsPublished { get; set; }
    public Guid CategoryId { get; set; }
    public decimal? CommerceHubPriceOverride { get; set; }
    public decimal Price { get; set; }
    public string? AttributesJson { get; set; }
}

/// <summary>
/// Raised when an existing CommerceHub listing changes.
/// </summary>
public class CommerceHubListingUpdatedEvent : IntegrationEvent
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public bool IsPublished { get; set; }
    public Guid CategoryId { get; set; }
    public decimal? CommerceHubPriceOverride { get; set; }
    public decimal Price { get; set; }
    public string? AttributesJson { get; set; }
}

/// <summary>
/// Raised when a CommerceHub listing is unpublished.
/// </summary>
public class CommerceHubListingUnpublishedEvent : IntegrationEvent
{
    public Guid ListingId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public bool IsPublished { get; set; }
    public Guid CategoryId { get; set; }
    public decimal? CommerceHubPriceOverride { get; set; }
    public decimal Price { get; set; }
}

public abstract class CommerceHubPriceOverrideChangedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid ListingId { get; set; }
    public Guid ActorUserId { get; set; }
    public decimal? PreviousValue { get; set; }
    public decimal? NewValue { get; set; }
    public int MutationVersion { get; set; }
    public string Reason { get; set; } = string.Empty;

    public Guid GetAuthoritativeAggregateId() => ListingId;
}

public sealed class CommerceHubPriceOverrideSavedEvent : CommerceHubPriceOverrideChangedEvent;

public sealed class CommerceHubPriceOverrideUpdatedEvent : CommerceHubPriceOverrideChangedEvent;

public sealed class CommerceHubPriceOverrideRemovedEvent : CommerceHubPriceOverrideChangedEvent;

/// <summary>
/// Raised when a CommerceHub marketplace order is created successfully.
/// Consumers use this as the trigger to project the customer-facing read model.
/// </summary>
public class CommerceHubMarketplaceOrderCreatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid MarketplaceOrderId { get; set; }

    /// <summary>
    /// All vendor/store tenants affected by this marketplace order.
    /// The base TenantId remains populated for backward compatibility, but
    /// multi-vendor consumers must fan out across this list.
    /// </summary>
    public List<Guid> AffectedStoreIds { get; set; } = new();

    public Guid GetAuthoritativeAggregateId() => MarketplaceOrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() => AffectedStoreIds;
}

/// <summary>
/// Raised when the marketplace order status or payment state changes.
/// </summary>
public class CommerceHubMarketplaceOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid MarketplaceOrderId { get; set; }
    public Guid? SubOrderId { get; set; }

    /// <summary>
    /// All vendor/store tenants whose order state can change because of this aggregate update.
    /// Multi-vendor consumers must use this list instead of the backward-compatible base TenantId.
    /// </summary>
    public List<Guid> AffectedStoreIds { get; set; } = new();

    public Guid GetAuthoritativeAggregateId() => MarketplaceOrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() => AffectedStoreIds;
}

/// <summary>
/// Raised when a sub-order status or payment state changes.
/// </summary>
public class CommerceHubSubOrderStatusUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent, IAffectedTenantScopedIntegrationEvent
{
    public Guid MarketplaceOrderId { get; set; }
    public Guid SubOrderId { get; set; }
    public Guid StoreId { get; set; }

    public Guid GetAuthoritativeAggregateId() => MarketplaceOrderId;

    public IReadOnlyCollection<Guid> GetAffectedTenantIds() =>
        StoreId == Guid.Empty ? Array.Empty<Guid>() : new[] { StoreId };
}
