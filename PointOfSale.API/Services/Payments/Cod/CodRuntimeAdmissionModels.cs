using System.Collections.ObjectModel;

namespace PointOfSale.API.Services.Payments.Cod;

public sealed record CodAdmissionId(string Value);

public sealed record CodAdmissionCorrelation(
    string? CorrelationId,
    string? CausationId,
    string? RequestId,
    string? TraceId);

public sealed record CodAdmissionEvidenceIdentity(
    string EvidenceId,
    string EvidenceType,
    string EvidenceSource);

public sealed record CodAdmissionEvidenceVersion(
    int Major,
    int Minor,
    string SchemaName,
    string? Revision);

public sealed record CodAdmissionMetadata(
    IReadOnlyDictionary<string, string> Values,
    string? SourceReference,
    string? ActorReference,
    string? Notes) : ICodAdmissionMetadata
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

public sealed record CodAdmissionCollectionReference(
    string CollectionId,
    string? OrderId,
    string? SubOrderId,
    string? ShipmentId,
    string? SettlementReference);

public sealed record CodAdmissionRuntimeContext(
    string TenantId,
    string Environment,
    string PaymentIdentity,
    string EvidenceIdentity,
    CodAdmissionCorrelation Correlation) : ICodAdmissionContext;

public sealed record CodRuntimeAdmissionRequest(
    CodAdmissionId AdmissionId,
    CodAdmissionCorrelation Correlation,
    CodAdmissionEvidenceIdentity EvidenceIdentity,
    CodAdmissionEvidenceVersion EvidenceVersion,
    CodAdmissionMetadata EvidenceMetadata,
    CodAdmissionCollectionReference CollectionReference,
    CodRequestedAdmissionOutcome RequestedOutcome,
    DateTimeOffset AdmissionTimestamp,
    CodAdmissionRuntimeContext RuntimeContext) : ICodAdmissionRequest;

public sealed record CodRuntimeAdmissionOutcome(
    CodAdmissionId AdmissionId,
    CodAdmissionOutcomeStatus Status,
    string Code,
    string Description,
    CodAdmissionMetadata Metadata) : ICodAdmissionOutcome;

public sealed record CodAdmissionOutcomeDefinition(
    CodAdmissionOutcomeStatus Status,
    string Code,
    string Description,
    bool TerminalForAdmissionOnly);

public enum CodRequestedAdmissionOutcome
{
    CollectionAccepted = 1,
    CollectionRejected = 2,
    CollectionDeferred = 3,
    CollectionIncomplete = 4,
    CollectionUnsupported = 5
}

public enum CodAdmissionOutcomeStatus
{
    Accepted = 1,
    Rejected = 2,
    Deferred = 3,
    Incomplete = 4,
    Unsupported = 5
}
