using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public enum ShipmentCancellationOutcome { Requested, Confirmed, ReconciliationRequired, Unsupported }

/// <summary>Permanent logistics operation; never a payment/refund authority.</summary>
public sealed class ShipmentCancellationOperation : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ActorIdentity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? AwbNumber { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? CarrierRequestId { get; set; }
    public Guid? ConfigurationId { get; set; }
    public string? AccountId { get; set; }
    public string? Environment { get; set; }
    public ShipmentStatus PreviousStatus { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? AttemptStartedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public ShipmentCancellationOutcome Outcome { get; set; }
    public string? ConfirmationBasis { get; set; }
    public string? EvidenceReference { get; set; }
    public string? EvidenceHash { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid Version { get; set; } = Guid.NewGuid();
}
