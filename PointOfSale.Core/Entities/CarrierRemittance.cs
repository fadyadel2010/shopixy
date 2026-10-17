using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>Merchant physical-receipt attestation, never provider payment verification.</summary>
public sealed class CarrierRemittance : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OperatorId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public Guid ConfigurationId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal Fees { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string ReceiptChannel { get; set; } = "PhysicalMerchantCash";
    public string ReceiptReference { get; set; } = string.Empty;
    public string ReceiptReferenceKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? EvidenceReference { get; set; }
    public bool ActualReceiptAttested { get; set; }
    public string ConfirmationBasis { get; set; } = "OperatorAttestedPhysicalReceipt";
    public string ReconciliationOutcome { get; set; } = "WholeOrderZeroFeeMatched";
    public Guid ReceivingAccountId { get; set; }
    public Guid ClearingAccountId { get; set; }
    public string Outcome { get; set; } = "Preparing";
    public Guid? PostedJournalId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public List<CarrierRemittanceOrder> Orders { get; set; } = [];
}

/// <summary>Immutable evidence lineage for the shared permanent settlement allocation.</summary>
public sealed class CarrierRemittanceOrder : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid RemittanceId { get; set; }
    public CarrierRemittance Remittance { get; set; } = null!;
    public Guid AllocationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ShipmentBindingId { get; set; }
    public Guid CollectionEvidenceId { get; set; }
    public Guid CollectionJournalId { get; set; }
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
