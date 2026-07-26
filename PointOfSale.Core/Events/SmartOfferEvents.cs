namespace PointOfSale.Core.Events;

public class SmartOfferCreatedEvent : IntegrationEvent
{
    public Guid SmartOfferId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SmartOfferUpdatedEvent : IntegrationEvent
{
    public Guid SmartOfferId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SmartOfferDeletedEvent : IntegrationEvent
{
    public Guid SmartOfferId { get; set; }
    public string Name { get; set; } = string.Empty;
}
