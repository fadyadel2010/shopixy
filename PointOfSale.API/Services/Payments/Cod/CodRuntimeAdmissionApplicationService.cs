using System.Collections.ObjectModel;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodRuntimeAdmissionApplicationService
{
    Task<CodRuntimeAdmissionPreparationResult> PrepareAdmissionAsync(
        CodRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default);

    Task<CodRuntimeAdmissionApplicationResult> CoordinateAdmissionAsync(
        CodRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CodRuntimeAdmissionApplicationService : ICodRuntimeAdmissionApplicationService
{
    private readonly ICodRuntimeAdmissionCatalog _foundationCatalog;
    private readonly ICodRuntimeAdmissionBridge _runtimeAdmissionBridge;
    private readonly PaymentRuntimeOrchestrator _paymentRuntimeOrchestrator;

    public CodRuntimeAdmissionApplicationService(ICodRuntimeAdmissionCatalog foundationCatalog)
        : this(foundationCatalog, new CodRuntimeAdmissionBridge(), new PaymentRuntimeOrchestrator())
    {
    }

    public CodRuntimeAdmissionApplicationService(
        ICodRuntimeAdmissionCatalog foundationCatalog,
        PaymentRuntimeOrchestrator paymentRuntimeOrchestrator)
        : this(foundationCatalog, new CodRuntimeAdmissionBridge(), paymentRuntimeOrchestrator)
    {
    }

    public CodRuntimeAdmissionApplicationService(
        ICodRuntimeAdmissionCatalog foundationCatalog,
        ICodRuntimeAdmissionBridge runtimeAdmissionBridge,
        PaymentRuntimeOrchestrator paymentRuntimeOrchestrator)
    {
        _foundationCatalog = foundationCatalog;
        _runtimeAdmissionBridge = runtimeAdmissionBridge;
        _paymentRuntimeOrchestrator = paymentRuntimeOrchestrator;
    }

    public Task<CodRuntimeAdmissionPreparationResult> PrepareAdmissionAsync(
        CodRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = CodRuntimeAdmissionApplicationValidation.Validate(request);
        if (issues.Count > 0)
        {
            return Task.FromResult(CodRuntimeAdmissionPreparationResult.Invalid(request, issues));
        }

        var requestedStatus = MapRequestedOutcome(request.RequestedOutcome);
        var outcomeDefinition = _foundationCatalog.OutcomeDefinitions
            .SingleOrDefault(outcome => outcome.Status == requestedStatus);

        if (outcomeDefinition is null)
        {
            return Task.FromResult(CodRuntimeAdmissionPreparationResult.Invalid(
                request,
                new[]
                {
                    new CodRuntimeAdmissionValidationIssue(
                        "requested_outcome",
                        "COD_RUNTIME_ADMISSION_OUTCOME_UNSUPPORTED",
                        "Requested admission outcome is not defined by the COD Runtime Admission Foundation.")
                }));
        }

        var metadata = new CodRuntimeAdmissionPreparationMetadata(
            ApplicationBoundary: nameof(CodRuntimeAdmissionApplicationService),
            FoundationBoundary: nameof(ICodRuntimeAdmissionCatalog),
            PreparedAtUtc: request.AdmissionTimestamp,
            RuntimeExecutionEnabled: _foundationCatalog.RuntimeExecutionEnabled,
            LifecycleMutationEnabled: _foundationCatalog.LifecycleMutationEnabled,
            SettlementEnabled: _foundationCatalog.SettlementEnabled);

        return Task.FromResult(CodRuntimeAdmissionPreparationResult.Prepared(
            request,
            outcomeDefinition,
            metadata));
    }

    public async Task<CodRuntimeAdmissionApplicationResult> CoordinateAdmissionAsync(
        CodRuntimeAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var preparation = await PrepareAdmissionAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!preparation.IsPrepared)
        {
            return CodRuntimeAdmissionApplicationResult.FromPreparation(preparation);
        }

        var runtimeRequest = _runtimeAdmissionBridge.TranslateRequest(preparation.Request);
        var runtimeResult = _paymentRuntimeOrchestrator.Execute(
            runtimeRequest.Context,
            runtimeRequest.Evidence,
            runtimeRequest.Action,
            runtimeRequest.Session);

        var outcome = _runtimeAdmissionBridge.TranslateResponse(preparation.Request, runtimeResult);
        return CodRuntimeAdmissionApplicationResult.FromOutcome(outcome);
    }

    private static CodAdmissionOutcomeStatus MapRequestedOutcome(CodRequestedAdmissionOutcome outcome)
    {
        return outcome switch
        {
            CodRequestedAdmissionOutcome.CollectionAccepted => CodAdmissionOutcomeStatus.Accepted,
            CodRequestedAdmissionOutcome.CollectionRejected => CodAdmissionOutcomeStatus.Rejected,
            CodRequestedAdmissionOutcome.CollectionDeferred => CodAdmissionOutcomeStatus.Deferred,
            CodRequestedAdmissionOutcome.CollectionIncomplete => CodAdmissionOutcomeStatus.Incomplete,
            CodRequestedAdmissionOutcome.CollectionUnsupported => CodAdmissionOutcomeStatus.Unsupported,
            _ => CodAdmissionOutcomeStatus.Unsupported
        };
    }
}

public sealed record CodRuntimeAdmissionApplicationResult(
    CodRuntimeAdmissionPreparationResult? PreparationResult,
    CodRuntimeAdmissionOutcome? Outcome)
{
    public static CodRuntimeAdmissionApplicationResult FromPreparation(
        CodRuntimeAdmissionPreparationResult preparationResult)
    {
        return new CodRuntimeAdmissionApplicationResult(preparationResult, null);
    }

    public static CodRuntimeAdmissionApplicationResult FromOutcome(
        CodRuntimeAdmissionOutcome outcome)
    {
        return new CodRuntimeAdmissionApplicationResult(null, outcome);
    }
}

public static class CodRuntimeAdmissionApplicationValidation
{
    public static IReadOnlyList<CodRuntimeAdmissionValidationIssue> Validate(CodRuntimeAdmissionRequest request)
    {
        var issues = new List<CodRuntimeAdmissionValidationIssue>();

        Require(request.RuntimeContext.TenantId, "tenant", "COD_RUNTIME_ADMISSION_TENANT_REQUIRED", issues);
        Require(request.RuntimeContext.Environment, "environment", "COD_RUNTIME_ADMISSION_ENVIRONMENT_REQUIRED", issues);
        Require(request.EvidenceIdentity.EvidenceId, "evidence_identity", "COD_RUNTIME_ADMISSION_EVIDENCE_REQUIRED", issues);
        Require(request.EvidenceIdentity.EvidenceType, "evidence_type", "COD_RUNTIME_ADMISSION_EVIDENCE_TYPE_REQUIRED", issues);
        Require(request.EvidenceIdentity.EvidenceSource, "evidence_source", "COD_RUNTIME_ADMISSION_EVIDENCE_SOURCE_REQUIRED", issues);
        Require(request.RuntimeContext.PaymentIdentity, "payment_identity", "COD_RUNTIME_ADMISSION_PAYMENT_IDENTITY_REQUIRED", issues);
        Require(request.CollectionReference.CollectionId, "collection_identity", "COD_RUNTIME_ADMISSION_COLLECTION_REQUIRED", issues);

        if (string.IsNullOrWhiteSpace(request.Correlation.CorrelationId) &&
            string.IsNullOrWhiteSpace(request.Correlation.RequestId) &&
            string.IsNullOrWhiteSpace(request.Correlation.TraceId))
        {
            issues.Add(new CodRuntimeAdmissionValidationIssue(
                "correlation",
                "COD_RUNTIME_ADMISSION_CORRELATION_REQUIRED",
                "At least one correlation, request, or trace identifier is required."));
        }

        if (request.EvidenceMetadata.Values.Count == 0 ||
            request.EvidenceMetadata.Values.Any(value =>
                string.IsNullOrWhiteSpace(value.Key) ||
                string.IsNullOrWhiteSpace(value.Value)))
        {
            issues.Add(new CodRuntimeAdmissionValidationIssue(
                "metadata",
                "COD_RUNTIME_ADMISSION_METADATA_REQUIRED",
                "Application admission preparation requires non-empty evidence metadata."));
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceMetadata.SourceReference))
        {
            issues.Add(new CodRuntimeAdmissionValidationIssue(
                "source_reference",
                "COD_RUNTIME_ADMISSION_SOURCE_REFERENCE_REQUIRED",
                "Application admission preparation requires a source reference."));
        }

        if (request.EvidenceVersion.Major <= 0 ||
            string.IsNullOrWhiteSpace(request.EvidenceVersion.SchemaName))
        {
            issues.Add(new CodRuntimeAdmissionValidationIssue(
                "evidence_version",
                "COD_RUNTIME_ADMISSION_EVIDENCE_VERSION_REQUIRED",
                "Application admission preparation requires a canonical evidence version."));
        }

        return issues;
    }

    private static void Require(
        string? value,
        string field,
        string code,
        ICollection<CodRuntimeAdmissionValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(new CodRuntimeAdmissionValidationIssue(
            field,
            code,
            $"{field} is required for COD Runtime Admission preparation."));
    }
}

public sealed record CodRuntimeAdmissionContext(
    string TenantId,
    string Environment,
    string PaymentIdentity,
    string EvidenceIdentity,
    string? CorrelationId,
    string? RequestId,
    string? TraceId);

public sealed record CodRuntimeAdmissionMetadata(
    IReadOnlyDictionary<string, string> Values,
    string SourceReference,
    string? ActorReference,
    string? Notes)
{
    private IReadOnlyDictionary<string, string> _values = Freeze(Values);

    public IReadOnlyDictionary<string, string> Values
    {
        get => _values;
        init => _values = Freeze(value);
    }

    private static IReadOnlyDictionary<string, string> Freeze(
        IReadOnlyDictionary<string, string>? values)
    {
        return new ReadOnlyDictionary<string, string>(
            values is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(values, StringComparer.Ordinal));
    }
}

public sealed record CodRuntimeAdmissionPreparationMetadata(
    string ApplicationBoundary,
    string FoundationBoundary,
    DateTimeOffset PreparedAtUtc,
    bool RuntimeExecutionEnabled,
    bool LifecycleMutationEnabled,
    bool SettlementEnabled);

public sealed record CodRuntimeAdmissionValidationIssue(
    string Field,
    string Code,
    string Message);

public sealed record CodRuntimeAdmissionPreparationResult(
    bool IsPrepared,
    CodRuntimeAdmissionRequest Request,
    CodAdmissionOutcomeDefinition? OutcomeDefinition,
    CodRuntimeAdmissionPreparationMetadata? Metadata,
    IReadOnlyList<CodRuntimeAdmissionValidationIssue> ValidationIssues)
{
    private IReadOnlyList<CodRuntimeAdmissionValidationIssue> _validationIssues =
        Freeze(ValidationIssues);

    public IReadOnlyList<CodRuntimeAdmissionValidationIssue> ValidationIssues
    {
        get => _validationIssues;
        init => _validationIssues = Freeze(value);
    }

    public static CodRuntimeAdmissionPreparationResult Prepared(
        CodRuntimeAdmissionRequest request,
        CodAdmissionOutcomeDefinition outcomeDefinition,
        CodRuntimeAdmissionPreparationMetadata metadata)
    {
        return new CodRuntimeAdmissionPreparationResult(
            true,
            request,
            outcomeDefinition,
            metadata,
            Array.Empty<CodRuntimeAdmissionValidationIssue>());
    }

    public static CodRuntimeAdmissionPreparationResult Invalid(
        CodRuntimeAdmissionRequest request,
        IReadOnlyList<CodRuntimeAdmissionValidationIssue> validationIssues)
    {
        return new CodRuntimeAdmissionPreparationResult(
            false,
            request,
            null,
            null,
            validationIssues);
    }

    private static IReadOnlyList<CodRuntimeAdmissionValidationIssue> Freeze(
        IReadOnlyList<CodRuntimeAdmissionValidationIssue>? values)
    {
        return new ReadOnlyCollection<CodRuntimeAdmissionValidationIssue>(
            values?.ToArray() ?? Array.Empty<CodRuntimeAdmissionValidationIssue>());
    }
}
