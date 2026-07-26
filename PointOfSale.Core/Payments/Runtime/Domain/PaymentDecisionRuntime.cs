using PointOfSale.Core.Payments.Runtime.Contracts;
using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum DecisionStatus
{
    Unknown = 0,
    Produced = 1,
    Invalid = 2
}

public enum DecisionSeverity
{
    Unknown = 0,
    Informational = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public readonly record struct VerifiedEvidenceId(Guid Value);

public readonly record struct DecisionReason(string Value);

public readonly record struct DecisionTraceId(string Value);

public readonly record struct DecisionTimestamp(DateTimeOffset Value);

public readonly record struct DecisionCorrelationId(string Value);

public readonly record struct DecisionReference(string Value);

public readonly record struct DecisionSource(string Value);

public sealed record DecisionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public DecisionMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record DecisionTrace
{
    public DecisionTraceId Id { get; init; }

    public IReadOnlyCollection<DecisionReason> Reasons { get; init; }

    public DecisionTrace()
    {
        Id = new DecisionTraceId(string.Empty);
        Reasons = Array.AsReadOnly(Array.Empty<DecisionReason>());
    }

    public DecisionTrace(DecisionTraceId id, IReadOnlyCollection<DecisionReason>? reasons = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Decision trace identifier is required.", nameof(id));
        }

        Id = new DecisionTraceId(id.Value.Trim());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<DecisionReason>());
    }
}

public sealed record DecisionResult
{
    public DecisionStatus Status { get; init; }

    public DecisionReason Reason { get; init; }

    public DecisionSeverity Severity { get; init; }

    public DecisionTrace Trace { get; init; }

    public DecisionMetadata Metadata { get; init; }

    public DecisionResult()
    {
        Reason = new DecisionReason(string.Empty);
        Trace = new DecisionTrace();
        Metadata = new DecisionMetadata();
    }

    public DecisionResult(
        DecisionStatus status,
        DecisionReason reason,
        DecisionSeverity severity,
        DecisionTrace trace,
        DecisionMetadata? metadata = null)
    {
        if (status == DecisionStatus.Unknown)
        {
            throw new ArgumentException("Decision status is required.", nameof(status));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Decision reason is required.", nameof(reason));
        }

        if (severity == DecisionSeverity.Unknown)
        {
            throw new ArgumentException("Decision severity is required.", nameof(severity));
        }

        Status = status;
        Reason = new DecisionReason(reason.Value.Trim());
        Severity = severity;
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Metadata = metadata ?? new DecisionMetadata();
    }
}

public sealed record PaymentDecision : IPaymentDecision
{
    public DecisionId DecisionId { get; init; }

    public DecisionType DecisionType { get; init; }

    public DecisionReason DecisionReason { get; init; }

    public DecisionTimestamp DecisionTime { get; init; }

    public DecisionSource DecisionSource { get; init; }

    public DecisionTrace DecisionTrace { get; init; }

    public DecisionMetadata DecisionMetadata { get; init; }

    public VerifiedEvidenceId VerifiedEvidenceId { get; init; }

    public DecisionCorrelationId CorrelationId { get; init; }

    public DecisionResult Result { get; init; }

    public DecisionId Id => DecisionId;

    public DecisionType Type => DecisionType;

    public PaymentDecision()
    {
        DecisionReason = new DecisionReason(string.Empty);
        DecisionSource = new DecisionSource(string.Empty);
        DecisionTrace = new DecisionTrace();
        DecisionMetadata = new DecisionMetadata();
        CorrelationId = new DecisionCorrelationId(string.Empty);
        Result = new DecisionResult();
    }

    private PaymentDecision(
        DecisionId decisionId,
        DecisionType decisionType,
        DecisionReason decisionReason,
        DecisionTimestamp decisionTime,
        DecisionSource decisionSource,
        DecisionTrace decisionTrace,
        DecisionMetadata decisionMetadata,
        VerifiedEvidenceId verifiedEvidenceId,
        DecisionCorrelationId correlationId,
        DecisionResult result)
    {
        DecisionId = decisionId;
        DecisionType = decisionType;
        DecisionReason = decisionReason;
        DecisionTime = decisionTime;
        DecisionSource = decisionSource;
        DecisionTrace = decisionTrace;
        DecisionMetadata = decisionMetadata;
        VerifiedEvidenceId = verifiedEvidenceId;
        CorrelationId = correlationId;
        Result = result;
    }

    public static PaymentDecision Create(
        DecisionId decisionId,
        DecisionType decisionType,
        DecisionReason decisionReason,
        DecisionTimestamp decisionTime,
        DecisionSource decisionSource,
        DecisionTrace decisionTrace,
        DecisionMetadata decisionMetadata,
        VerifiedEvidenceId verifiedEvidenceId,
        DecisionCorrelationId correlationId,
        DecisionResult result)
    {
        RequireGuid(decisionId.Value, nameof(decisionId));
        RequireGuid(verifiedEvidenceId.Value, nameof(verifiedEvidenceId));
        RequireText(decisionReason.Value, nameof(decisionReason));
        RequireText(decisionSource.Value, nameof(decisionSource));
        RequireText(correlationId.Value, nameof(correlationId));

        if (decisionType == DecisionType.Unknown)
        {
            throw new ArgumentException("Decision type is required.", nameof(decisionType));
        }

        if (decisionTime.Value == default)
        {
            throw new ArgumentException("Decision timestamp is required.", nameof(decisionTime));
        }

        if (result.Status != DecisionStatus.Produced)
        {
            throw new ArgumentException("Decision result must be produced.", nameof(result));
        }

        if (result.Trace != decisionTrace)
        {
            throw new ArgumentException("Decision result trace must match the decision trace.", nameof(result));
        }

        return new PaymentDecision(
            decisionId,
            decisionType,
            new DecisionReason(decisionReason.Value.Trim()),
            decisionTime,
            new DecisionSource(decisionSource.Value.Trim()),
            decisionTrace,
            decisionMetadata,
            verifiedEvidenceId,
            new DecisionCorrelationId(correlationId.Value.Trim()),
            result);
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

public sealed class PaymentDecisionRuntime
{
    private const string RuntimeSource = "runtime-verification";

    public PaymentDecision Decide(VerifiedEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons = new List<DecisionReason>();
        Validate(evidence, reasons);

        var decisionId = new DecisionId(PaymentRuntimeDeterminism.CreateId(
            "decision",
            evidence.VerificationId.Value.ToString("N"),
            evidence.VerificationResult.Status.ToString(),
            string.Join(',', evidence.VerificationResult.Failures.Order())));
        var trace = new DecisionTrace(
            new DecisionTraceId($"decision:{decisionId.Value:N}"),
            reasons.Count == 0 ? [new DecisionReason("DECISION_STARTED")] : reasons);

        if (reasons.Count > 0)
        {
            throw new ArgumentException("Verified evidence is not eligible for decision production.", nameof(evidence));
        }

        var decisionType = Map(evidence.VerificationResult);
        var reason = ReasonFor(decisionType, evidence.VerificationResult);
        var severity = SeverityFor(decisionType);

        return CreateDecision(
            decisionId,
            decisionType,
            reason,
            severity,
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new DecisionReason(reason)]).ToArray()) },
            evidence);
    }

    private static PaymentDecision CreateDecision(
        DecisionId decisionId,
        DecisionType decisionType,
        string reason,
        DecisionSeverity severity,
        DecisionTrace trace,
        VerifiedEvidence evidence)
    {
        var metadata = new DecisionMetadata(new Dictionary<string, string>
        {
            ["evidenceId"] = evidence.EvidenceId.Value.ToString("D"),
            ["verificationId"] = evidence.VerificationId.Value.ToString("D"),
            ["verificationStatus"] = evidence.VerificationResult.Status.ToString()
        });

        var result = DecisionResultFactory.Create(
            DecisionStatus.Produced,
            new DecisionReason(reason),
            severity,
            trace,
            metadata);

        return PaymentDecisionFactory.Create(
            decisionId,
            decisionType,
            new DecisionReason(reason),
            new DecisionTimestamp(evidence.VerificationTime.Value),
            new DecisionSource(RuntimeSource),
            trace,
            metadata,
            new VerifiedEvidenceId(evidence.VerificationId.Value),
            new DecisionCorrelationId(evidence.EvidenceId.Value.ToString("N")),
            result);
    }

    private static void Validate(VerifiedEvidence evidence, ICollection<DecisionReason> reasons)
    {
        if (evidence.EvidenceId.Value == Guid.Empty || evidence.VerificationId.Value == Guid.Empty)
        {
            reasons.Add(new DecisionReason("IDENTIFIER_INVALID"));
        }

        if (evidence.VerificationTime.Value == default)
        {
            reasons.Add(new DecisionReason("VERIFICATION_NOT_COMPLETED"));
        }

        if (evidence.VerificationResult.Status == VerificationStatus.Unknown)
        {
            reasons.Add(new DecisionReason("VERIFICATION_STATUS_INVALID"));
        }

        if (evidence.VerificationResult.EvidenceId != evidence.EvidenceId
            || evidence.VerificationResult.VerificationId != evidence.VerificationId)
        {
            reasons.Add(new DecisionReason("VERIFICATION_RESULT_MISMATCH"));
        }

        if (string.IsNullOrWhiteSpace(evidence.VerificationTrace.Id.Value))
        {
            reasons.Add(new DecisionReason("TRACE_INVALID"));
        }
    }

    private static DecisionType Map(VerificationResult result)
    {
        if (result.Status == VerificationStatus.Verified)
        {
            return DecisionType.Approved;
        }

        if (result.Failures.Contains(VerificationFailure.DuplicateEvidence))
        {
            return DecisionType.Quarantine;
        }

        if (result.Failures.Contains(VerificationFailure.OrderingInvalid))
        {
            return DecisionType.Recovery;
        }

        return DecisionType.Rejected;
    }

    private static string ReasonFor(DecisionType decisionType, VerificationResult result)
    {
        if (decisionType == DecisionType.Approved)
        {
            return "VERIFICATION_ACCEPTED";
        }

        var firstReason = result.Reasons.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstReason.Value))
        {
            return firstReason.Value;
        }

        return decisionType switch
        {
            DecisionType.Quarantine => "VERIFICATION_DUPLICATE",
            DecisionType.Recovery => "VERIFICATION_ORDERING",
            _ => "VERIFICATION_REJECTED"
        };
    }

    private static DecisionSeverity SeverityFor(DecisionType decisionType)
    {
        return decisionType switch
        {
            DecisionType.Approved => DecisionSeverity.Informational,
            DecisionType.Quarantine => DecisionSeverity.Critical,
            DecisionType.Recovery => DecisionSeverity.Warning,
            _ => DecisionSeverity.Error
        };
    }
}

public static class PaymentDecisionFactory
{
    public static PaymentDecision Create(
        DecisionId decisionId,
        DecisionType decisionType,
        DecisionReason decisionReason,
        DecisionTimestamp decisionTime,
        DecisionSource decisionSource,
        DecisionTrace decisionTrace,
        DecisionMetadata decisionMetadata,
        VerifiedEvidenceId verifiedEvidenceId,
        DecisionCorrelationId correlationId,
        DecisionResult result)
    {
        return PaymentDecision.Create(
            decisionId,
            decisionType,
            decisionReason,
            decisionTime,
            decisionSource,
            decisionTrace,
            decisionMetadata,
            verifiedEvidenceId,
            correlationId,
            result);
    }
}

public static class DecisionResultFactory
{
    public static DecisionResult Create(
        DecisionStatus status,
        DecisionReason reason,
        DecisionSeverity severity,
        DecisionTrace trace,
        DecisionMetadata? metadata = null)
    {
        return new DecisionResult(status, reason, severity, trace, metadata);
    }
}
