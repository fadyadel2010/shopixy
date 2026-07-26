using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Customs;

public enum CustomsEvidenceStatus
{
    Candidate = 0,
    Admitted = 1,
    Superseded = 2,
    Retired = 3,
    Rejected = 4,
    Draft = 5,
    UnderReview = 6,
    Approved = 7,
    Published = 8,
    Reopened = 9
}

public enum CustomsEvidenceSourceKind
{
    Product = 0,
    ProductVariant = 1,
    OrderLine = 2,
    System = 3
}

public enum CustomsAdmissionAuditAction
{
    EvidenceAdmitted = 0,
    EvidenceRejected = 1,
    EvidenceVersionCreated = 2,
    ShipmentSnapshotCreated = 3,
    ShipmentSnapshotReused = 4,
    ShipmentSnapshotRejected = 5,
    CarrierNeutralDtoEmitted = 6,
    RecoveryRejected = 7
}

public enum CustomsAdministrationAuditAction
{
    DraftCreated = 0,
    DraftEdited = 1,
    SubmittedForReview = 2,
    Approved = 3,
    Rejected = 4,
    Reopened = 5,
    Published = 6,
    Retired = 7,
    Superseded = 8,
    AdmissionValidated = 9,
    AdmissionRejected = 10
}

public enum CustomsPublicationAction
{
    Published = 0,
    Retired = 1,
    Superseded = 2
}

public sealed class CustomsEvidence : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public CustomsEvidenceSourceKind SourceKind { get; set; }
    public Guid SourceId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public CustomsEvidenceStatus Status { get; set; } = CustomsEvidenceStatus.Admitted;
    public Guid CurrentVersionId { get; set; }
    public int CurrentVersionNumber { get; set; }
    public DateTime AdmittedAtUtc { get; set; }
    public string AdmittedBy { get; set; } = "system";
    public string CorrelationId { get; set; } = string.Empty;
    public string CreatedByActor { get; set; } = "system";
    public string LastEditedByActor { get; set; } = "system";
    public string? SubmittedByActor { get; set; }

    public List<CustomsEvidenceVersion> Versions { get; set; } = new();
}

public sealed class CustomsEvidenceVersion : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public CustomsEvidence Evidence { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string HsCode { get; set; } = string.Empty;
    public string CommodityDescription { get; set; } = string.Empty;
    public string CountryOfOriginIso2 { get; set; } = string.Empty;
    public string? CountryOfManufactureIso2 { get; set; }
    public decimal DeclaredValue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal NetWeight { get; set; }
    public string WeightUnit { get; set; } = string.Empty;
    public string QuantityUnit { get; set; } = string.Empty;
    public bool? DangerousGoods { get; set; }
    public string? ExportClassification { get; set; }
    public string ValueSource { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
    public string EvidenceFingerprintSha256 { get; set; } = string.Empty;
    public DateTime AdmittedAtUtc { get; set; }
    public string AdmittedBy { get; set; } = "system";
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class CustomsEvidenceHistory : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public Guid CustomsEvidenceVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Actor { get; set; } = "system";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class CustomsEvidenceFingerprint : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public Guid CustomsEvidenceVersionId { get; set; }
    public string FingerprintSha256 { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ShipmentCustomsSnapshot : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public int SnapshotVersion { get; set; } = 1;
    public int CarrierNeutralDtoVersion { get; set; } = 1;
    public string DestinationCountryIso2 { get; set; } = string.Empty;
    public string OriginCountryIso2 { get; set; } = string.Empty;
    public decimal ShipmentGrossWeight { get; set; }
    public string ShipmentGrossWeightUnit { get; set; } = string.Empty;
    public string SnapshotFingerprintSha256 { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public bool IsRegistrationFrozen { get; set; } = true;
    public DateTime RegistrationBeganAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;

    public List<ShipmentCustomsSnapshotLine> Lines { get; set; } = new();
}

public sealed class ShipmentCustomsSnapshotLine : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ShipmentCustomsSnapshotId { get; set; }
    public ShipmentCustomsSnapshot Snapshot { get; set; } = null!;
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public Guid CustomsEvidenceVersionId { get; set; }
    public int EvidenceVersionNumber { get; set; }
    public string HsCode { get; set; } = string.Empty;
    public string CommodityDescription { get; set; } = string.Empty;
    public string CountryOfOriginIso2 { get; set; } = string.Empty;
    public string? CountryOfManufactureIso2 { get; set; }
    public decimal DeclaredValue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string QuantityUnit { get; set; } = string.Empty;
    public decimal NetWeight { get; set; }
    public string NetWeightUnit { get; set; } = string.Empty;
    public bool? DangerousGoods { get; set; }
    public string? ExportClassification { get; set; }
    public string SourceEvidenceFingerprintSha256 { get; set; } = string.Empty;
}

public sealed class CustomsAdmissionAudit : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public CustomsAdmissionAuditAction Action { get; set; }
    public Guid? CustomsEvidenceId { get; set; }
    public Guid? CustomsEvidenceVersionId { get; set; }
    public Guid? ShipmentCustomsSnapshotId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string Actor { get; set; } = "system";
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? SourceEvidenceFingerprintSha256 { get; set; }
    public string? FailureCode { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class CustomsEvidenceApproval : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public Guid CustomsEvidenceVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string ValidationChecksumSha256 { get; set; } = string.Empty;
    public string ApprovedByActor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; set; }
}

public sealed class CustomsEvidencePublication : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CustomsEvidenceId { get; set; }
    public Guid CustomsEvidenceVersionId { get; set; }
    public Guid? SupersededCustomsEvidenceId { get; set; }
    public Guid? SupersededCustomsEvidenceVersionId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public int VersionNumber { get; set; }
    public CustomsPublicationAction Action { get; set; }
    public string ValidationChecksumSha256 { get; set; } = string.Empty;
    public string PublishedByActor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class CustomsPublicationLock : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string LockKey { get; set; } = string.Empty;
    public DateTime LockedAtUtc { get; set; }
    public DateTime LockedUntilUtc { get; set; }
    public string LockedByActor { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class CustomsAdministrationAudit : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public CustomsAdministrationAuditAction Action { get; set; }
    public Guid? CustomsEvidenceId { get; set; }
    public Guid? CustomsEvidenceVersionId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string Actor { get; set; } = "system";
    public string Role { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? ValidationChecksumSha256 { get; set; }
    public string? FailureCode { get; set; }
    public CustomsEvidenceStatus? FromStatus { get; set; }
    public CustomsEvidenceStatus? ToStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
