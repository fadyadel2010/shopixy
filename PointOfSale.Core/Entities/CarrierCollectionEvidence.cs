using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>Permanent observed customer money, or an immutable review decision. This is never merchant-remittance evidence.</summary>
public sealed class CarrierCollectionEvidence : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ConfigurationId { get; set; }
    public Guid? ShipmentBindingId { get; set; }
    public Guid? ReviewedEvidenceId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string DeliveryReference { get; set; } = string.Empty;
    public decimal? ObservedAmount { get; set; }
    public string? Currency { get; set; }
    public DateTime ProviderEventAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string SourceContract { get; set; } = string.Empty;
    public string TrustMethod { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ObservationFingerprint { get; set; } = string.Empty;
    public string AdmissionOutcome { get; set; } = string.Empty;
    public string AdmissionCode { get; set; } = string.Empty;
    public string AuditIdentity { get; set; } = string.Empty;
}
