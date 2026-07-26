using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Application-owned durable replay record for canonical Paymob webhook evidence.
/// Runtime receives prior evidence supplied from this table, but remains storage-unaware.
/// </summary>
public class PaymobWebhookReplayRecord : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string Provider { get; set; } = "paymob";

    public string ProviderEventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public Guid EvidenceId { get; set; }

    public Guid ActionId { get; set; }

    public string EvidenceHash { get; set; } = string.Empty;

    public string EvidenceReference { get; set; } = string.Empty;

    public string ProviderReference { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime ProcessingTimestampUtc { get; set; }

    public Guid? PaymentIntentId { get; set; }

    public Guid? OrderId { get; set; }

    public string AdmissionClassification { get; set; } = string.Empty;
}
