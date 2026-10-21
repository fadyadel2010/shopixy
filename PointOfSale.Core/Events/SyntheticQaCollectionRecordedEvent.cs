namespace PointOfSale.Core.Events;

/// <summary>QA-only virtual accounting outbox fact. NEVER a payment-captured fact.</summary>
public sealed class SyntheticQaCollectionRecordedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid OrderId { get; set; }
    public Guid EvidenceId { get; set; }
    public Guid RunId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Provenance { get; set; } = Entities.SyntheticQaEvidence.ProvenanceLabel;
    public Guid GetAuthoritativeAggregateId() => OrderId;
}
