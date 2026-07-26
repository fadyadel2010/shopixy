using System.Security.Cryptography;
using System.Text;
using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum VerificationStatus
{
    Unknown = 0,
    Verified = 1,
    Failed = 2
}

public enum VerificationFailure
{
    None = 0,
    IntegrityInvalid = 1,
    StructureInvalid = 2,
    RequiredFieldMissing = 3,
    CorrelationInvalid = 4,
    OwnershipInvalid = 5,
    DuplicateEvidence = 6,
    OrderingInvalid = 7,
    HashInvalid = 8
}

public readonly record struct VerificationId(Guid Value);

public readonly record struct VerificationHash(string Value);

public readonly record struct VerificationReason(string Value);

public readonly record struct VerificationTimestamp(DateTimeOffset Value);

public readonly record struct VerificationTraceId(string Value);

public readonly record struct AdmissionReference(string Value);

public sealed record VerificationTrace
{
    public VerificationTraceId Id { get; init; }

    public IReadOnlyCollection<VerificationReason> Reasons { get; init; }

    public VerificationTrace()
    {
        Id = new VerificationTraceId(string.Empty);
        Reasons = Array.AsReadOnly(Array.Empty<VerificationReason>());
    }

    public VerificationTrace(VerificationTraceId id, IReadOnlyCollection<VerificationReason>? reasons = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Verification trace identifier is required.", nameof(id));
        }

        Id = new VerificationTraceId(id.Value.Trim());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<VerificationReason>());
    }
}

public sealed record VerificationMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public VerificationMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = Normalize(values);
    }

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record AdmittedEvidence
{
    public EvidenceId EvidenceId { get; init; }

    public PaymentActionId ActionId { get; init; }

    public EvidenceType EvidenceType { get; init; }

    public EvidenceStatus EvidenceStatus { get; init; }

    public EvidenceReference EvidenceReference { get; init; }

    public EvidenceSource EvidenceSource { get; init; }

    public EvidenceTimestamp EvidenceTimestamp { get; init; }

    public EvidenceHash EvidenceHash { get; init; }

    public ProviderPayloadReference PayloadReference { get; init; }

    public ActionCorrelationId CorrelationId { get; init; }

    public AdmissionReference AdmissionReference { get; init; }

    public EvidenceMetadata Metadata { get; init; }

    public AdmittedEvidence()
    {
        EvidenceReference = new EvidenceReference(string.Empty);
        EvidenceSource = new EvidenceSource(string.Empty);
        EvidenceHash = new EvidenceHash(string.Empty);
        PayloadReference = new ProviderPayloadReference(string.Empty);
        CorrelationId = new ActionCorrelationId(string.Empty);
        AdmissionReference = new AdmissionReference(string.Empty);
        Metadata = new EvidenceMetadata();
    }

    private AdmittedEvidence(
        EvidenceId evidenceId,
        PaymentActionId actionId,
        EvidenceType evidenceType,
        EvidenceStatus evidenceStatus,
        EvidenceReference evidenceReference,
        EvidenceSource evidenceSource,
        EvidenceTimestamp evidenceTimestamp,
        EvidenceHash evidenceHash,
        ProviderPayloadReference payloadReference,
        ActionCorrelationId correlationId,
        AdmissionReference admissionReference,
        EvidenceMetadata metadata)
    {
        EvidenceId = evidenceId;
        ActionId = actionId;
        EvidenceType = evidenceType;
        EvidenceStatus = evidenceStatus;
        EvidenceReference = evidenceReference;
        EvidenceSource = evidenceSource;
        EvidenceTimestamp = evidenceTimestamp;
        EvidenceHash = evidenceHash;
        PayloadReference = payloadReference;
        CorrelationId = correlationId;
        AdmissionReference = admissionReference;
        Metadata = metadata;
    }

    public static AdmittedEvidence Create(
        EvidenceId evidenceId,
        PaymentActionId actionId,
        EvidenceType evidenceType,
        EvidenceStatus evidenceStatus,
        EvidenceReference evidenceReference,
        EvidenceSource evidenceSource,
        EvidenceTimestamp evidenceTimestamp,
        EvidenceHash evidenceHash,
        ProviderPayloadReference payloadReference,
        ActionCorrelationId correlationId,
        AdmissionReference admissionReference,
        EvidenceMetadata? metadata = null)
    {
        RequireGuid(evidenceId.Value, nameof(evidenceId));
        RequireGuid(actionId.Value, nameof(actionId));
        RequireText(evidenceReference.Value, nameof(evidenceReference));
        RequireText(evidenceSource.Value, nameof(evidenceSource));
        RequireText(correlationId.Value, nameof(correlationId));
        RequireText(admissionReference.Value, nameof(admissionReference));

        if (evidenceType == EvidenceType.Unknown)
        {
            throw new ArgumentException("Evidence type is required.", nameof(evidenceType));
        }

        if (evidenceStatus == EvidenceStatus.Unknown)
        {
            throw new ArgumentException("Evidence status is required.", nameof(evidenceStatus));
        }

        if (evidenceTimestamp.Value == default)
        {
            throw new ArgumentException("Evidence timestamp is required.", nameof(evidenceTimestamp));
        }

        return new AdmittedEvidence(
            evidenceId,
            actionId,
            evidenceType,
            evidenceStatus,
            new EvidenceReference(evidenceReference.Value.Trim()),
            new EvidenceSource(evidenceSource.Value.Trim()),
            evidenceTimestamp,
            new EvidenceHash(evidenceHash.Value.Trim()),
            new ProviderPayloadReference(payloadReference.Value.Trim()),
            new ActionCorrelationId(correlationId.Value.Trim()),
            new AdmissionReference(admissionReference.Value.Trim()),
            metadata ?? new EvidenceMetadata());
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public sealed record AdmissionResult
{
    public EvidenceId EvidenceId { get; init; }

    public bool Admitted { get; init; }

    public AdmittedEvidence? Evidence { get; init; }

    public IReadOnlyCollection<VerificationReason> Reasons { get; init; }

    public AdmissionResult()
    {
        Reasons = Array.AsReadOnly(Array.Empty<VerificationReason>());
    }

    private AdmissionResult(
        EvidenceId evidenceId,
        bool admitted,
        AdmittedEvidence? evidence,
        IReadOnlyCollection<VerificationReason> reasons)
    {
        EvidenceId = evidenceId;
        Admitted = admitted;
        Evidence = evidence;
        Reasons = reasons;
    }

    public static AdmissionResult Accepted(AdmittedEvidence evidence)
    {
        return new AdmissionResult(evidence.EvidenceId, true, evidence, Array.AsReadOnly(Array.Empty<VerificationReason>()));
    }

    public static AdmissionResult Rejected(EvidenceId evidenceId, IReadOnlyCollection<VerificationReason> reasons)
    {
        return new AdmissionResult(evidenceId, false, null, Array.AsReadOnly(reasons.ToArray()));
    }
}

public sealed record VerificationResult
{
    public EvidenceId EvidenceId { get; init; }

    public VerificationId VerificationId { get; init; }

    public VerificationStatus Status { get; init; }

    public IReadOnlyCollection<VerificationFailure> Failures { get; init; }

    public IReadOnlyCollection<VerificationReason> Reasons { get; init; }

    public VerificationTrace Trace { get; init; }

    public VerificationMetadata Metadata { get; init; }

    public bool Accepted => Status == VerificationStatus.Verified;

    public VerificationResult()
    {
        Failures = Array.AsReadOnly(Array.Empty<VerificationFailure>());
        Reasons = Array.AsReadOnly(Array.Empty<VerificationReason>());
        Trace = new VerificationTrace();
        Metadata = new VerificationMetadata();
    }

    public VerificationResult(
        EvidenceId evidenceId,
        VerificationId verificationId,
        VerificationStatus status,
        IReadOnlyCollection<VerificationFailure>? failures,
        IReadOnlyCollection<VerificationReason>? reasons,
        VerificationTrace trace,
        VerificationMetadata? metadata = null)
    {
        if (verificationId.Value == Guid.Empty)
        {
            throw new ArgumentException("Verification identifier is required.", nameof(verificationId));
        }

        if (status == VerificationStatus.Unknown)
        {
            throw new ArgumentException("Verification status is required.", nameof(status));
        }

        EvidenceId = evidenceId;
        VerificationId = verificationId;
        Status = status;
        Failures = Array.AsReadOnly(failures?.ToArray() ?? Array.Empty<VerificationFailure>());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<VerificationReason>());
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Metadata = metadata ?? new VerificationMetadata();
    }
}

public sealed record VerifiedEvidence
{
    public EvidenceId EvidenceId { get; init; }

    public VerificationId VerificationId { get; init; }

    public VerificationTimestamp VerificationTime { get; init; }

    public VerificationHash EvidenceHash { get; init; }

    public VerificationResult VerificationResult { get; init; }

    public VerificationTrace VerificationTrace { get; init; }

    public VerificationMetadata VerificationMetadata { get; init; }

    public VerifiedEvidence()
    {
        EvidenceHash = new VerificationHash(string.Empty);
        VerificationResult = new VerificationResult();
        VerificationTrace = new VerificationTrace();
        VerificationMetadata = new VerificationMetadata();
    }

    private VerifiedEvidence(
        EvidenceId evidenceId,
        VerificationId verificationId,
        VerificationTimestamp verificationTime,
        VerificationHash evidenceHash,
        VerificationResult verificationResult,
        VerificationTrace verificationTrace,
        VerificationMetadata verificationMetadata)
    {
        EvidenceId = evidenceId;
        VerificationId = verificationId;
        VerificationTime = verificationTime;
        EvidenceHash = evidenceHash;
        VerificationResult = verificationResult;
        VerificationTrace = verificationTrace;
        VerificationMetadata = verificationMetadata;
    }

    public static VerifiedEvidence Create(
        EvidenceId evidenceId,
        VerificationId verificationId,
        VerificationTimestamp verificationTime,
        VerificationHash evidenceHash,
        VerificationResult verificationResult,
        VerificationTrace verificationTrace,
        VerificationMetadata? verificationMetadata = null)
    {
        RequireGuid(evidenceId.Value, nameof(evidenceId));
        RequireGuid(verificationId.Value, nameof(verificationId));
        RequireText(evidenceHash.Value, nameof(evidenceHash));

        if (verificationTime.Value == default)
        {
            throw new ArgumentException("Verification timestamp is required.", nameof(verificationTime));
        }

        if (verificationResult.EvidenceId != evidenceId || verificationResult.VerificationId != verificationId)
        {
            throw new ArgumentException("Verification result must belong to the verified evidence.", nameof(verificationResult));
        }

        return new VerifiedEvidence(
            evidenceId,
            verificationId,
            verificationTime,
            evidenceHash,
            verificationResult,
            verificationTrace,
            verificationMetadata ?? new VerificationMetadata());
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public sealed record EvidenceVerificationContext(
    PaymentAction Action,
    IReadOnlyCollection<AdmittedEvidence>? PriorEvidence = null);

public sealed class EvidenceAdmissionRuntime
{
    public AdmissionResult Admit(ProviderEvidence evidence, PaymentAction action)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(action);

        var reasons = new List<VerificationReason>();

        if (evidence.ActionId != action.Id)
        {
            reasons.Add(new VerificationReason("OWNERSHIP_INVALID"));
        }

        if (evidence.Id.Value == Guid.Empty || evidence.ActionId.Value == Guid.Empty)
        {
            reasons.Add(new VerificationReason("IDENTIFIER_INVALID"));
        }

        if (evidence.Timestamp.Value == default || evidence.Timestamp.Value < action.CreatedUtc)
        {
            reasons.Add(new VerificationReason("TIMESTAMP_INVALID"));
        }

        if (string.IsNullOrWhiteSpace(evidence.Reference.Value)
            || string.IsNullOrWhiteSpace(evidence.Source.Value)
            || string.IsNullOrWhiteSpace(evidence.PayloadReference.Value))
        {
            reasons.Add(new VerificationReason("REFERENCE_INVALID"));
        }

        if (string.IsNullOrWhiteSpace(evidence.CorrelationId.Value)
            || evidence.CorrelationId != action.CorrelationId)
        {
            reasons.Add(new VerificationReason("CORRELATION_INVALID"));
        }

        if (evidence.Type == EvidenceType.Unknown || evidence.Status == EvidenceStatus.Unknown)
        {
            reasons.Add(new VerificationReason("STRUCTURE_INVALID"));
        }

        if (reasons.Count > 0)
        {
            return AdmissionResult.Rejected(evidence.Id, reasons);
        }

        var admitted = AdmittedEvidenceFactory.Create(
            evidence.Id,
            evidence.ActionId,
            evidence.Type,
            EvidenceStatus.Admitted,
            evidence.Reference,
            evidence.Source,
            evidence.Timestamp,
            new EvidenceHash(string.Empty),
            evidence.PayloadReference,
            evidence.CorrelationId,
            new AdmissionReference($"admission:{evidence.Id.Value:N}"),
            evidence.Metadata);

        admitted = admitted with { EvidenceHash = new EvidenceHash(CanonicalEvidenceHasher.Compute(admitted)) };

        return AdmissionResult.Accepted(admitted);
    }
}

public sealed class EvidenceVerificationRuntime
{
    public VerifiedEvidence Verify(AdmittedEvidence evidence, EvidenceVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Action);

        var verificationId = new VerificationId(PaymentRuntimeDeterminism.CreateId(
            "verification",
            evidence.EvidenceId.Value.ToString("N"),
            evidence.EvidenceHash.Value));
        var trace = new VerificationTrace(
            new VerificationTraceId($"verification:{verificationId.Value:N}"),
            [new VerificationReason("VERIFICATION_STARTED")]);

        var failures = new List<VerificationFailure>();
        var reasons = new List<VerificationReason>();
        var prior = context.PriorEvidence?.ToArray() ?? Array.Empty<AdmittedEvidence>();

        ValidateIntegrity(evidence, failures, reasons);
        ValidateStructure(evidence, failures, reasons);
        ValidateRequiredFields(evidence, failures, reasons);
        ValidateCorrelation(evidence, context.Action, failures, reasons);
        ValidateOwnership(evidence, context.Action, failures, reasons);
        ValidateDuplicates(evidence, prior, failures, reasons);
        ValidateOrdering(evidence, prior, failures, reasons);
        ValidateHash(evidence, failures, reasons);

        var status = failures.Count == 0 ? VerificationStatus.Verified : VerificationStatus.Failed;
        var result = new VerificationResult(
            evidence.EvidenceId,
            verificationId,
            status,
            failures,
            reasons,
            trace,
            new VerificationMetadata(new Dictionary<string, string>
            {
                ["evidenceType"] = evidence.EvidenceType.ToString(),
                ["actionId"] = evidence.ActionId.Value.ToString("D")
            }));

        return VerifiedEvidenceFactory.Create(
            evidence.EvidenceId,
            verificationId,
            new VerificationTimestamp(evidence.EvidenceTimestamp.Value),
            new VerificationHash(CanonicalEvidenceHasher.Compute(evidence)),
            result,
            trace,
            result.Metadata);
    }

    private static void ValidateIntegrity(
        AdmittedEvidence evidence,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (evidence.EvidenceStatus != EvidenceStatus.Admitted)
        {
            Add(failures, reasons, VerificationFailure.IntegrityInvalid, "EVIDENCE_NOT_ADMITTED");
        }
    }

    private static void ValidateStructure(
        AdmittedEvidence evidence,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (evidence.EvidenceType == EvidenceType.Unknown)
        {
            Add(failures, reasons, VerificationFailure.StructureInvalid, "EVIDENCE_TYPE_INVALID");
        }
    }

    private static void ValidateRequiredFields(
        AdmittedEvidence evidence,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (evidence.EvidenceId.Value == Guid.Empty
            || evidence.ActionId.Value == Guid.Empty
            || evidence.EvidenceTimestamp.Value == default
            || string.IsNullOrWhiteSpace(evidence.EvidenceReference.Value)
            || string.IsNullOrWhiteSpace(evidence.EvidenceSource.Value)
            || string.IsNullOrWhiteSpace(evidence.PayloadReference.Value))
        {
            Add(failures, reasons, VerificationFailure.RequiredFieldMissing, "REQUIRED_FIELD_MISSING");
        }
    }

    private static void ValidateCorrelation(
        AdmittedEvidence evidence,
        PaymentAction action,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (string.IsNullOrWhiteSpace(evidence.CorrelationId.Value) || evidence.CorrelationId != action.CorrelationId)
        {
            Add(failures, reasons, VerificationFailure.CorrelationInvalid, "CORRELATION_INVALID");
        }
    }

    private static void ValidateOwnership(
        AdmittedEvidence evidence,
        PaymentAction action,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (evidence.ActionId != action.Id)
        {
            Add(failures, reasons, VerificationFailure.OwnershipInvalid, "OWNERSHIP_INVALID");
        }
    }

    private static void ValidateDuplicates(
        AdmittedEvidence evidence,
        IReadOnlyCollection<AdmittedEvidence> prior,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (prior.Any(item =>
            item.EvidenceId == evidence.EvidenceId
            || item.EvidenceReference == evidence.EvidenceReference
            || (!string.IsNullOrWhiteSpace(item.EvidenceHash.Value) && item.EvidenceHash == evidence.EvidenceHash)))
        {
            Add(failures, reasons, VerificationFailure.DuplicateEvidence, "DUPLICATE_EVIDENCE");
        }
    }

    private static void ValidateOrdering(
        AdmittedEvidence evidence,
        IReadOnlyCollection<AdmittedEvidence> prior,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        if (prior.Any(item => item.ActionId == evidence.ActionId && item.EvidenceTimestamp.Value > evidence.EvidenceTimestamp.Value))
        {
            Add(failures, reasons, VerificationFailure.OrderingInvalid, "ORDERING_INVALID");
        }
    }

    private static void ValidateHash(
        AdmittedEvidence evidence,
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons)
    {
        var expected = CanonicalEvidenceHasher.Compute(evidence);
        if (!string.Equals(evidence.EvidenceHash.Value, expected, StringComparison.Ordinal))
        {
            Add(failures, reasons, VerificationFailure.HashInvalid, "HASH_INVALID");
        }
    }

    private static void Add(
        ICollection<VerificationFailure> failures,
        ICollection<VerificationReason> reasons,
        VerificationFailure failure,
        string reason)
    {
        failures.Add(failure);
        reasons.Add(new VerificationReason(reason));
    }
}

public static class AdmittedEvidenceFactory
{
    public static AdmittedEvidence Create(
        EvidenceId evidenceId,
        PaymentActionId actionId,
        EvidenceType evidenceType,
        EvidenceStatus evidenceStatus,
        EvidenceReference evidenceReference,
        EvidenceSource evidenceSource,
        EvidenceTimestamp evidenceTimestamp,
        EvidenceHash evidenceHash,
        ProviderPayloadReference payloadReference,
        ActionCorrelationId correlationId,
        AdmissionReference admissionReference,
        EvidenceMetadata? metadata = null)
    {
        return AdmittedEvidence.Create(
            evidenceId,
            actionId,
            evidenceType,
            evidenceStatus,
            evidenceReference,
            evidenceSource,
            evidenceTimestamp,
            evidenceHash,
            payloadReference,
            correlationId,
            admissionReference,
            metadata);
    }
}

public static class VerifiedEvidenceFactory
{
    public static VerifiedEvidence Create(
        EvidenceId evidenceId,
        VerificationId verificationId,
        VerificationTimestamp verificationTime,
        VerificationHash evidenceHash,
        VerificationResult verificationResult,
        VerificationTrace verificationTrace,
        VerificationMetadata? verificationMetadata = null)
    {
        return VerifiedEvidence.Create(
            evidenceId,
            verificationId,
            verificationTime,
            evidenceHash,
            verificationResult,
            verificationTrace,
            verificationMetadata);
    }
}

public static class CanonicalEvidenceHasher
{
    public static string Compute(AdmittedEvidence evidence)
    {
        var canonical = string.Join(
            "|",
            evidence.EvidenceId.Value.ToString("N"),
            evidence.ActionId.Value.ToString("N"),
            evidence.EvidenceType,
            evidence.EvidenceStatus,
            evidence.EvidenceReference.Value.Trim(),
            evidence.EvidenceSource.Value.Trim(),
            evidence.EvidenceTimestamp.Value.ToUniversalTime().ToString("O"),
            evidence.PayloadReference.Value.Trim(),
            evidence.CorrelationId.Value.Trim());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
