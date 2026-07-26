namespace PointOfSale.Core.Events;

public class ProductMediaItemData
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public int Position { get; set; }
}

public class ProductMediaChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public List<ProductMediaItemData> Media { get; set; } = new();
}
