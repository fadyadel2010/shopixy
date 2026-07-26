using PointOfSale.Core.DTOs.Customs;
using PointOfSale.Core.Entities.Customs;

namespace PointOfSale.Core.Interfaces.Customs;

public sealed record CustomsEvidenceAdmissionRequest(
    Guid TenantId,
    CustomsEvidenceSourceKind SourceKind,
    Guid SourceId,
    Guid? ProductId,
    Guid? ProductVariantId,
    string HsCode,
    string CommodityDescription,
    string CountryOfOriginIso2,
    string? CountryOfManufactureIso2,
    decimal DeclaredValue,
    string Currency,
    decimal NetWeight,
    string WeightUnit,
    string QuantityUnit,
    bool? DangerousGoods,
    string? ExportClassification,
    string ValueSource,
    string SourceEvidenceJson,
    string Actor,
    string CorrelationId);

public sealed record CustomsEvidenceAdmissionResult(
    bool Succeeded,
    Guid? CustomsEvidenceId,
    Guid? CustomsEvidenceVersionId,
    int? VersionNumber,
    string? FailureCode,
    string? FailureMessage);

public sealed record ShipmentCustomsSnapshotRequest(
    Guid TenantId,
    Guid ShipmentId,
    Guid OrderId,
    string OriginCountryIso2,
    string DestinationCountryIso2,
    decimal ShipmentGrossWeight,
    string ShipmentGrossWeightUnit,
    string Actor,
    string CorrelationId);

public sealed record ShipmentCustomsSnapshotResult(
    bool Succeeded,
    Guid? ShipmentCustomsSnapshotId,
    CarrierNeutralCustomsDto? CarrierNeutralDto,
    string? FailureCode,
    string? FailureMessage);

public interface ICustomsEvidenceAdmissionService
{
    Task<CustomsEvidenceAdmissionResult> AdmitEvidenceAsync(
        CustomsEvidenceAdmissionRequest request,
        CancellationToken cancellationToken = default);

    Task<ShipmentCustomsSnapshotResult> CreateOrReuseShipmentSnapshotAsync(
        ShipmentCustomsSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<ShipmentCustomsSnapshotResult> RecoverShipmentSnapshotAsync(
        Guid tenantId,
        Guid shipmentId,
        string actor,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public enum CustomsAdministrationRole
{
    Editor = 0,
    Reviewer = 1,
    Approver = 2,
    PublicationOperator = 3,
    AdmissionService = 4
}

public sealed record CustomsAdministrationActor(
    string ActorId,
    IReadOnlyCollection<CustomsAdministrationRole> Roles);

public sealed record CustomsWorkflowEvidencePayload(
    CustomsEvidenceSourceKind SourceKind,
    Guid SourceId,
    Guid? ProductId,
    Guid? ProductVariantId,
    string HsCode,
    string CommodityDescription,
    string CountryOfOriginIso2,
    string? CountryOfManufactureIso2,
    decimal DeclaredValue,
    string Currency,
    decimal NetWeight,
    string WeightUnit,
    string QuantityUnit,
    bool? DangerousGoods,
    string? ExportClassification,
    string ValueSource,
    string SourceEvidenceJson);

public sealed record CustomsWorkflowResult(
    bool Succeeded,
    Guid? CustomsEvidenceId,
    Guid? CustomsEvidenceVersionId,
    int? VersionNumber,
    CustomsEvidenceStatus? Status,
    string? ValidationChecksumSha256,
    string? FailureCode,
    string? FailureMessage);

public sealed record CustomsAdmissionValidationRequest(
    Guid TenantId,
    Guid CustomsEvidenceId,
    Guid CustomsEvidenceVersionId,
    Guid ProductId,
    Guid? ProductVariantId,
    int VersionNumber,
    string ValidationChecksumSha256,
    CustomsAdministrationActor Actor,
    string CorrelationId);

public interface ICustomsAdministrationWorkflowService
{
    Task<CustomsWorkflowResult> CreateDraftAsync(
        Guid tenantId,
        CustomsWorkflowEvidencePayload payload,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> EditDraftAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsWorkflowEvidencePayload payload,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> SubmitForReviewAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> ApproveAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        string validationChecksumSha256,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> RejectAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> ReopenAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> PublishAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> RetireAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> SupersedeAsync(
        Guid tenantId,
        Guid customsEvidenceId,
        Guid replacementCustomsEvidenceId,
        CustomsAdministrationActor actor,
        string reason,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustomsWorkflowResult> ValidateAdmissionAsync(
        CustomsAdmissionValidationRequest request,
        CancellationToken cancellationToken = default);
}
