namespace PointOfSale.Core.Entities;

public class CommerceHubPaymentLog : BaseEntity
{
    public Guid PaymentId { get; set; }
    public CommerceHubPayment Payment { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public string? ProviderEventId { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}
