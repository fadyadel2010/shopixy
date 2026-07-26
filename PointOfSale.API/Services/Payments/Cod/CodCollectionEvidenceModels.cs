using System.Collections.ObjectModel;

namespace PointOfSale.API.Services.Payments.Cod;

public sealed record CodCollectionId(string Value);

public sealed record CodEvidenceId(string Value);

public sealed record CodCollectionAmount(decimal Value);

public sealed record CodCurrency(string Code);

public sealed record CodEvidenceReference(string Value);

public sealed record CodEvidenceCorrelation(
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    string? OrderId,
    string? SubOrderId,
    string? ShipmentId,
    string? ActorId);

public sealed record CodEvidenceVersion(
    int Major,
    int Minor,
    string SchemaName);

public sealed record CodEvidenceIntegrity(
    string? IntegrityMarker,
    string? HashAlgorithm,
    string? EvidenceHash,
    bool RequiresIntegrityMarker);

public sealed record CodCollectionEvidenceMetadata(
    IReadOnlyDictionary<string, string> Values,
    string? SourceReference,
    string? ActorReference,
    string? Notes) : ICodEvidenceMetadata
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

public sealed record CodCollectionEvidence(
    CodCollectionId CollectionId,
    CodEvidenceId EvidenceId,
    CodCollectionEvidenceSource Source,
    CodCollectionEvidenceType Type,
    CodCollectionAmount Amount,
    CodCurrency Currency,
    DateTimeOffset CollectedAt,
    DateTimeOffset RecordedAt,
    CodEvidenceReference Reference,
    CodCollectionEvidenceMetadata Metadata,
    CodEvidenceCorrelation Correlation,
    CodEvidenceVersion Version,
    CodEvidenceIntegrity Integrity,
    CodCollectionEvidenceStatus Status) : ICodCollectionEvidence;

public sealed record CodEvidenceSourceDefinition(
    CodCollectionEvidenceSource Source,
    string Code,
    string DisplayName,
    string AcceptedRole,
    string RejectedRole) : ICodEvidenceSource;

public sealed record CodEvidenceTypeDefinition(
    CodCollectionEvidenceType Type,
    string Code,
    string DisplayName,
    string Definition);

public sealed record CodEvidenceValidationRuleDefinition(
    CodEvidenceValidationRule Rule,
    string Code,
    string Description,
    bool Required) : ICodEvidenceValidationContract;

public enum CodCollectionEvidenceSource
{
    Driver = 1,
    Courier = 2,
    Carrier = 3,
    PosAdmin = 4,
    CommerceHub = 5,
    ManualEntry = 6
}

public enum CodCollectionEvidenceType
{
    CashCollection = 1,
    CollectionAttempt = 2,
    CollectionFailure = 3,
    PartialCollection = 4,
    CustomerRefusal = 5,
    CarrierConfirmation = 6,
    AdminConfirmation = 7,
    Dispute = 8,
    ReconciliationMarker = 9
}

public enum CodCollectionEvidenceStatus
{
    Draft = 1,
    Recorded = 2,
    Superseded = 3,
    Rejected = 4,
    Disputed = 5
}

public enum CodEvidenceValidationRule
{
    RequiredAmount = 1,
    RequiredCurrency = 2,
    RequiredEvidenceSource = 3,
    RequiredReference = 4,
    RequiredTimestamp = 5,
    RequiredIntegrityMarker = 6,
    RequiredCollectionIdentity = 7
}
