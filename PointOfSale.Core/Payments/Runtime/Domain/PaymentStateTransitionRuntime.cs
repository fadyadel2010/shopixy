using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum TransitionStatus
{
    Unknown = 0,
    Applied = 1,
    Rejected = 2,
    Replayed = 3
}

public enum TransitionFailure
{
    None = 0,
    CurrentStateInvalid = 1,
    DecisionInvalid = 2,
    TransitionIllegal = 3,
    OwnershipInvalid = 4,
    DuplicateTransition = 5,
    ReplayDetected = 6
}

public readonly record struct TransitionId(Guid Value);

public readonly record struct TransitionReference(string Value);

public readonly record struct TransitionTimestamp(DateTimeOffset Value);

public readonly record struct TransitionTraceId(string Value);

public readonly record struct TransitionReason(string Value);

public sealed record TransitionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public TransitionMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record TransitionTrace
{
    public TransitionTraceId Id { get; init; }

    public IReadOnlyCollection<TransitionReason> Reasons { get; init; }

    public TransitionTrace()
    {
        Id = new TransitionTraceId(string.Empty);
        Reasons = Array.AsReadOnly(Array.Empty<TransitionReason>());
    }

    public TransitionTrace(TransitionTraceId id, IReadOnlyCollection<TransitionReason>? reasons = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Transition trace identifier is required.", nameof(id));
        }

        Id = new TransitionTraceId(id.Value.Trim());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<TransitionReason>());
    }
}

public sealed record StateTransition
{
    public TransitionId TransitionId { get; init; }

    public PaymentSessionId SessionId { get; init; }

    public DecisionId DecisionId { get; init; }

    public PaymentStatus FromStatus { get; init; }

    public PaymentStatus ToStatus { get; init; }

    public TransitionReason Reason { get; init; }

    public TransitionTimestamp Timestamp { get; init; }

    public TransitionTrace Trace { get; init; }

    public TransitionMetadata Metadata { get; init; }

    public TransitionReference Reference { get; init; }

    public StateTransition()
    {
        Reason = new TransitionReason(string.Empty);
        Trace = new TransitionTrace();
        Metadata = new TransitionMetadata();
        Reference = new TransitionReference(string.Empty);
    }

    private StateTransition(
        TransitionId transitionId,
        PaymentSessionId sessionId,
        DecisionId decisionId,
        PaymentStatus fromStatus,
        PaymentStatus toStatus,
        TransitionReason reason,
        TransitionTimestamp timestamp,
        TransitionTrace trace,
        TransitionMetadata metadata,
        TransitionReference reference)
    {
        TransitionId = transitionId;
        SessionId = sessionId;
        DecisionId = decisionId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        Timestamp = timestamp;
        Trace = trace;
        Metadata = metadata;
        Reference = reference;
    }

    public static StateTransition Create(
        TransitionId transitionId,
        PaymentSessionId sessionId,
        DecisionId decisionId,
        PaymentStatus fromStatus,
        PaymentStatus toStatus,
        TransitionReason reason,
        TransitionTimestamp timestamp,
        TransitionTrace trace,
        TransitionMetadata? metadata = null,
        TransitionReference? reference = null)
    {
        RequireGuid(transitionId.Value, nameof(transitionId));
        RequireGuid(sessionId.Value, nameof(sessionId));
        RequireGuid(decisionId.Value, nameof(decisionId));
        RequireText(reason.Value, nameof(reason));

        if (fromStatus == PaymentStatus.Unknown || toStatus == PaymentStatus.Unknown)
        {
            throw new ArgumentException("Transition statuses must be canonical.", nameof(toStatus));
        }

        if (fromStatus == toStatus)
        {
            throw new ArgumentException("Transition must change state.", nameof(toStatus));
        }

        if (timestamp.Value == default)
        {
            throw new ArgumentException("Transition timestamp is required.", nameof(timestamp));
        }

        var resolvedReference = reference ?? new TransitionReference($"transition:{transitionId.Value:N}");
        RequireText(resolvedReference.Value, nameof(reference));

        return new StateTransition(
            transitionId,
            sessionId,
            decisionId,
            fromStatus,
            toStatus,
            new TransitionReason(reason.Value.Trim()),
            timestamp,
            trace ?? throw new ArgumentNullException(nameof(trace)),
            metadata ?? new TransitionMetadata(),
            new TransitionReference(resolvedReference.Value.Trim()));
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

public sealed record TransitionResult
{
    public TransitionStatus Status { get; init; }

    public PaymentSession Session { get; init; }

    public StateTransition? Transition { get; init; }

    public IReadOnlyCollection<TransitionFailure> Failures { get; init; }

    public TransitionReason Reason { get; init; }

    public TransitionTrace Trace { get; init; }

    public TransitionMetadata Metadata { get; init; }

    public TransitionResult()
    {
        Session = new PaymentSession();
        Failures = Array.AsReadOnly(Array.Empty<TransitionFailure>());
        Reason = new TransitionReason(string.Empty);
        Trace = new TransitionTrace();
        Metadata = new TransitionMetadata();
    }

    public TransitionResult(
        TransitionStatus status,
        PaymentSession session,
        StateTransition? transition,
        IReadOnlyCollection<TransitionFailure>? failures,
        TransitionReason reason,
        TransitionTrace trace,
        TransitionMetadata? metadata = null)
    {
        if (status == TransitionStatus.Unknown)
        {
            throw new ArgumentException("Transition status is required.", nameof(status));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Transition reason is required.", nameof(reason));
        }

        Status = status;
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Transition = transition;
        Failures = Array.AsReadOnly(failures?.ToArray() ?? Array.Empty<TransitionFailure>());
        Reason = new TransitionReason(reason.Value.Trim());
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Metadata = metadata ?? new TransitionMetadata();
    }
}

public static class StateTransitionFactory
{
    public static StateTransition Create(
        TransitionId transitionId,
        PaymentSessionId sessionId,
        DecisionId decisionId,
        PaymentStatus fromStatus,
        PaymentStatus toStatus,
        TransitionReason reason,
        TransitionTimestamp timestamp,
        TransitionTrace trace,
        TransitionMetadata? metadata = null,
        TransitionReference? reference = null)
    {
        return StateTransition.Create(
            transitionId,
            sessionId,
            decisionId,
            fromStatus,
            toStatus,
            reason,
            timestamp,
            trace,
            metadata,
            reference);
    }
}

public static class TransitionResultFactory
{
    public static TransitionResult Create(
        TransitionStatus status,
        PaymentSession session,
        StateTransition? transition,
        IReadOnlyCollection<TransitionFailure>? failures,
        TransitionReason reason,
        TransitionTrace trace,
        TransitionMetadata? metadata = null)
    {
        return new TransitionResult(status, session, transition, failures, reason, trace, metadata);
    }
}

public sealed class StateTransitionRuntime
{
    private static readonly IReadOnlyDictionary<PaymentStatus, IReadOnlyCollection<PaymentStatus>> LegalTransitions =
        new Dictionary<PaymentStatus, IReadOnlyCollection<PaymentStatus>>
        {
            [PaymentStatus.Created] = [PaymentStatus.Pending, PaymentStatus.Cancelled, PaymentStatus.Expired],
            [PaymentStatus.Pending] = [PaymentStatus.AwaitingCustomer, PaymentStatus.AwaitingProvider, PaymentStatus.Rejected, PaymentStatus.Cancelled, PaymentStatus.Expired, PaymentStatus.ManualReview, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.AwaitingCustomer] = [PaymentStatus.Verified, PaymentStatus.Rejected, PaymentStatus.Cancelled, PaymentStatus.Expired, PaymentStatus.ManualReview, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.AwaitingProvider] = [PaymentStatus.Verified, PaymentStatus.Rejected, PaymentStatus.Cancelled, PaymentStatus.Expired, PaymentStatus.ManualReview, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.Verified] = [PaymentStatus.Approved, PaymentStatus.Rejected, PaymentStatus.ManualReview, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.Approved] = [PaymentStatus.Completed, PaymentStatus.SettlementPending, PaymentStatus.Cancelled, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.Completed] = [PaymentStatus.SettlementPending, PaymentStatus.RefundPending, PaymentStatus.Settled],
            [PaymentStatus.SettlementPending] = [PaymentStatus.Settled, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.Settled] = [PaymentStatus.RefundPending],
            [PaymentStatus.RefundPending] = [PaymentStatus.Refunded, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.ManualReview] = [PaymentStatus.Verified, PaymentStatus.Approved, PaymentStatus.Rejected, PaymentStatus.Cancelled, PaymentStatus.Recovery, PaymentStatus.Quarantined],
            [PaymentStatus.Recovery] = [PaymentStatus.Pending, PaymentStatus.AwaitingProvider, PaymentStatus.ManualReview, PaymentStatus.Quarantined],
            [PaymentStatus.Quarantined] = [PaymentStatus.ManualReview, PaymentStatus.Recovery],
            [PaymentStatus.Authorized] = [PaymentStatus.Approved, PaymentStatus.Completed, PaymentStatus.SettlementPending],
            [PaymentStatus.Confirmed] = [PaymentStatus.Completed, PaymentStatus.SettlementPending],
            [PaymentStatus.Captured] = [PaymentStatus.Completed, PaymentStatus.SettlementPending],
            [PaymentStatus.Collected] = [PaymentStatus.Completed, PaymentStatus.SettlementPending, PaymentStatus.Settled],
            [PaymentStatus.ActionRequired] = [PaymentStatus.AwaitingCustomer, PaymentStatus.Cancelled, PaymentStatus.Expired],
            [PaymentStatus.AwaitingConfirmation] = [PaymentStatus.AwaitingProvider, PaymentStatus.Verified, PaymentStatus.Rejected]
        };

    public TransitionResult Transition(
        PaymentDecision decision,
        PaymentSession session,
        IReadOnlyCollection<StateTransition>? priorTransitions = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(session);

        var targetStatus = ResolveTargetStatus(session.Status, decision.DecisionType);
        var transitionId = new TransitionId(PaymentRuntimeDeterminism.CreateId(
            "transition",
            session.Id.Value.ToString("N"),
            decision.DecisionId.Value.ToString("N"),
            session.Status.ToString(),
            targetStatus.ToString()));
        var trace = new TransitionTrace(
            new TransitionTraceId($"transition:{transitionId.Value:N}"),
            [new TransitionReason("TRANSITION_STARTED")]);
        var failures = new List<TransitionFailure>();
        var prior = priorTransitions?.ToArray() ?? Array.Empty<StateTransition>();

        ValidateCurrentState(session, failures);
        ValidateDecision(decision, session, failures);
        ValidateOwnership(session, prior, failures);
        ValidateHistory(session, prior, failures);
        ValidateUniqueness(transitionId, decision, prior, failures);
        ValidateReplay(decision, prior, failures);

        if (failures.Contains(TransitionFailure.ReplayDetected))
        {
            return CreateResult(TransitionStatus.Replayed, session, null, failures, "TRANSITION_REPLAYED", trace, decision);
        }

        if (failures.Count > 0)
        {
            return CreateResult(TransitionStatus.Rejected, session, null, failures, "TRANSITION_INVALID", trace, decision);
        }

        if (!IsLegal(session.Status, targetStatus))
        {
            failures.Add(TransitionFailure.TransitionIllegal);
            return CreateResult(TransitionStatus.Rejected, session, null, failures, "TRANSITION_ILLEGAL", trace, decision);
        }

        var reason = ReasonFor(decision, targetStatus);
        var metadata = MetadataFor(session, decision);
        var transition = StateTransitionFactory.Create(
            transitionId,
            session.Id,
            decision.DecisionId,
            session.Status,
            targetStatus,
            new TransitionReason(reason),
            new TransitionTimestamp(decision.DecisionTime.Value),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new TransitionReason(reason)]).ToArray()) },
            metadata);

        var updated = session with
        {
            Status = targetStatus,
            LastUpdatedUtc = transition.Timestamp.Value,
            AuditReferences = AppendAuditReference(session.AuditReferences, decision)
        };

        return TransitionResultFactory.Create(
            TransitionStatus.Applied,
            updated,
            transition,
            Array.Empty<TransitionFailure>(),
            new TransitionReason(reason),
            transition.Trace,
            metadata);
    }

    private static void ValidateCurrentState(PaymentSession session, ICollection<TransitionFailure> failures)
    {
        if (session.Id.Value == Guid.Empty || session.Status == PaymentStatus.Unknown)
        {
            failures.Add(TransitionFailure.CurrentStateInvalid);
        }
    }

    private static void ValidateDecision(
        PaymentDecision decision,
        PaymentSession session,
        ICollection<TransitionFailure> failures)
    {
        if (decision.DecisionId.Value == Guid.Empty
            || decision.DecisionType == DecisionType.Unknown
            || decision.DecisionTime.Value == default
            || decision.Result.Status != DecisionStatus.Produced
            || decision.DecisionTime.Value < session.LastUpdatedUtc)
        {
            failures.Add(TransitionFailure.DecisionInvalid);
        }
    }

    private static void ValidateHistory(
        PaymentSession session,
        IReadOnlyCollection<StateTransition> prior,
        ICollection<TransitionFailure> failures)
    {
        if (prior.Count == 0)
        {
            return;
        }

        var latest = prior.OrderByDescending(transition => transition.Timestamp.Value).First();
        if (latest.ToStatus != session.Status || latest.Timestamp.Value > session.LastUpdatedUtc)
        {
            failures.Add(TransitionFailure.CurrentStateInvalid);
        }
    }

    private static void ValidateOwnership(
        PaymentSession session,
        IReadOnlyCollection<StateTransition> prior,
        ICollection<TransitionFailure> failures)
    {
        if (prior.Any(transition => transition.SessionId != session.Id))
        {
            failures.Add(TransitionFailure.OwnershipInvalid);
        }
    }

    private static void ValidateUniqueness(
        TransitionId transitionId,
        PaymentDecision decision,
        IReadOnlyCollection<StateTransition> prior,
        ICollection<TransitionFailure> failures)
    {
        if (prior.Any(transition => transition.TransitionId == transitionId || transition.DecisionId == decision.DecisionId))
        {
            failures.Add(TransitionFailure.DuplicateTransition);
        }
    }

    private static void ValidateReplay(
        PaymentDecision decision,
        IReadOnlyCollection<StateTransition> prior,
        ICollection<TransitionFailure> failures)
    {
        if (prior.Any(transition => transition.DecisionId == decision.DecisionId))
        {
            failures.Add(TransitionFailure.ReplayDetected);
        }
    }

    internal static bool IsLegal(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        return LegalTransitions.TryGetValue(fromStatus, out var targets)
            && targets.Contains(toStatus);
    }

    internal static PaymentStatus ResolveTargetStatus(PaymentStatus currentStatus, DecisionType decisionType)
    {
        return decisionType switch
        {
            DecisionType.Approved => NextApprovedStatus(currentStatus),
            DecisionType.Rejected => PaymentStatus.Rejected,
            DecisionType.ManualReview or DecisionType.FraudReview or DecisionType.ComplianceReview => PaymentStatus.ManualReview,
            DecisionType.Retry or DecisionType.Recovery => PaymentStatus.Recovery,
            DecisionType.Cancelled => PaymentStatus.Cancelled,
            DecisionType.Expired => PaymentStatus.Expired,
            DecisionType.Quarantine => PaymentStatus.Quarantined,
            _ => PaymentStatus.Unknown
        };
    }

    private static PaymentStatus NextApprovedStatus(PaymentStatus currentStatus)
    {
        return currentStatus switch
        {
            PaymentStatus.Created => PaymentStatus.Pending,
            PaymentStatus.Pending => PaymentStatus.AwaitingCustomer,
            PaymentStatus.ActionRequired => PaymentStatus.AwaitingCustomer,
            PaymentStatus.AwaitingConfirmation => PaymentStatus.AwaitingProvider,
            PaymentStatus.AwaitingCustomer or PaymentStatus.AwaitingProvider => PaymentStatus.Verified,
            PaymentStatus.Verified or PaymentStatus.ManualReview or PaymentStatus.Authorized => PaymentStatus.Approved,
            PaymentStatus.Approved or PaymentStatus.Confirmed or PaymentStatus.Captured => PaymentStatus.Completed,
            PaymentStatus.Completed or PaymentStatus.Collected => PaymentStatus.SettlementPending,
            PaymentStatus.SettlementPending => PaymentStatus.Settled,
            PaymentStatus.RefundPending => PaymentStatus.Refunded,
            _ => PaymentStatus.Unknown
        };
    }

    private static string ReasonFor(PaymentDecision decision, PaymentStatus targetStatus)
    {
        return $"{decision.DecisionType.ToString().ToUpperInvariant()}_TO_{targetStatus.ToString().ToUpperInvariant()}";
    }

    private static TransitionMetadata MetadataFor(PaymentSession session, PaymentDecision decision)
    {
        return new TransitionMetadata(new Dictionary<string, string>
        {
            ["sessionId"] = session.Id.Value.ToString("D"),
            ["decisionId"] = decision.DecisionId.Value.ToString("D"),
            ["decisionType"] = decision.DecisionType.ToString(),
            ["fromStatus"] = session.Status.ToString()
        });
    }

    private static AuditReferences AppendAuditReference(AuditReferences references, PaymentDecision decision)
    {
        var values = references.Values
            .Concat([new CorrelationId($"decision:{decision.DecisionId.Value:N}")])
            .ToArray();

        return new AuditReferences(values);
    }

    private static TransitionResult CreateResult(
        TransitionStatus status,
        PaymentSession session,
        StateTransition? transition,
        IReadOnlyCollection<TransitionFailure> failures,
        string reason,
        TransitionTrace trace,
        PaymentDecision decision)
    {
        var metadata = new TransitionMetadata(new Dictionary<string, string>
        {
            ["sessionId"] = session.Id.Value.ToString("D"),
            ["decisionId"] = decision.DecisionId.Value.ToString("D"),
            ["currentStatus"] = session.Status.ToString(),
            ["decisionType"] = decision.DecisionType.ToString()
        });

        return TransitionResultFactory.Create(
            status,
            session,
            transition,
            failures,
            new TransitionReason(reason),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new TransitionReason(reason)]).ToArray()) },
            metadata);
    }
}
