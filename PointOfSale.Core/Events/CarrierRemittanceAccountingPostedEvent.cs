namespace PointOfSale.Core.Events;

public sealed class CarrierRemittanceAccountingPostedEvent : IntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid RemittanceId { get; set; }
    public Guid JournalId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ConfirmationBasis { get; set; } = "OperatorAttestedPhysicalReceipt";
    public Guid GetAuthoritativeAggregateId() => RemittanceId;
}
