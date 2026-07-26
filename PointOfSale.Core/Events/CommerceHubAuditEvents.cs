namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a sellable variant is deleted and dependent projections must retire it.
/// </summary>
public class VariantDeletedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }

    public Guid GetAuthoritativeAggregateId() => VariantId != Guid.Empty
        ? VariantId
        : throw new InvalidOperationException("VariantDeletedEvent must carry a non-empty VariantId.");
}

/// <summary>
/// Event raised when a product becomes archived/non-sellable and downstream projections
/// should unpublish dependent listings.
/// </summary>
public class ProductArchivedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
}
