using PointOfSale.Core.Payments.Runtime.Contracts;
using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum TransactionType
{
    Unknown = 0,
    Authorization = 1,
    Capture = 2,
    Sale = 3,
    Refund = 4,
    PartialRefund = 5,
    Void = 6,
    Adjustment = 7,
    Chargeback = 8,
    Reversal = 9,
    SettlementReservation = 10
}

public enum TransactionStatus
{
    Unknown = 0,
    Created = 1,
    Rejected = 2,
    Replayed = 3,
    Duplicate = 4
}

public enum TransactionFailure
{
    None = 0,
    DecisionMissing = 1,
    TransitionMissing = 2,
    TransitionNotApplied = 3,
    CurrencyInvalid = 4,
    AmountInvalid = 5,
    IdentifierInvalid = 6,
    OwnershipInvalid = 7,
    ReplayDetected = 8,
    DuplicateTransaction = 9
}

public readonly record struct TransactionReference(string Value);

public readonly record struct TransactionAmount(decimal Value);

public readonly record struct TransactionReason(string Value);

public readonly record struct TransactionTimestamp(DateTimeOffset Value);

public readonly record struct TransactionTraceId(string Value);

public sealed record TransactionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public TransactionMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record TransactionTrace
{
    public TransactionTraceId Id { get; init; }

    public IReadOnlyCollection<TransactionReason> Reasons { get; init; }

    public TransactionTrace()
    {
        Id = new TransactionTraceId(string.Empty);
        Reasons = Array.AsReadOnly(Array.Empty<TransactionReason>());
    }

    public TransactionTrace(TransactionTraceId id, IReadOnlyCollection<TransactionReason>? reasons = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Transaction trace identifier is required.", nameof(id));
        }

        Id = new TransactionTraceId(id.Value.Trim());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<TransactionReason>());
    }
}

public sealed record PaymentTransaction : IPaymentTransaction
{
    public TransactionId TransactionId { get; init; }

    public TransactionType TransactionType { get; init; }

    public TransactionStatus TransactionStatus { get; init; }

    public TransactionAmount TransactionAmount { get; init; }

    public Currency Currency { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public CorrelationId CorrelationId { get; init; }

    public PaymentSessionId PaymentSessionId { get; init; }

    public DecisionId PaymentDecisionId { get; init; }

    public TransitionId TransitionId { get; init; }

    public TransactionMetadata Metadata { get; init; }

    public TransactionReference Reference { get; init; }

    public TransactionId Id => TransactionId;

    public PaymentSessionId SessionId => PaymentSessionId;

    public PaymentTransaction()
    {
        Currency = new Currency("EGP");
        CorrelationId = new CorrelationId(string.Empty);
        Metadata = new TransactionMetadata();
        Reference = new TransactionReference(string.Empty);
    }

    private PaymentTransaction(
        TransactionId transactionId,
        TransactionType transactionType,
        TransactionStatus transactionStatus,
        TransactionAmount transactionAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        DecisionId paymentDecisionId,
        TransitionId transitionId,
        TransactionMetadata metadata,
        TransactionReference reference)
    {
        TransactionId = transactionId;
        TransactionType = transactionType;
        TransactionStatus = transactionStatus;
        TransactionAmount = transactionAmount;
        Currency = currency;
        CreatedUtc = createdUtc;
        CorrelationId = correlationId;
        PaymentSessionId = paymentSessionId;
        PaymentDecisionId = paymentDecisionId;
        TransitionId = transitionId;
        Metadata = metadata;
        Reference = reference;
    }

    public static PaymentTransaction Create(
        TransactionId transactionId,
        TransactionType transactionType,
        TransactionStatus transactionStatus,
        TransactionAmount transactionAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        DecisionId paymentDecisionId,
        TransitionId transitionId,
        TransactionMetadata? metadata = null,
        TransactionReference? reference = null)
    {
        RequireGuid(transactionId.Value, nameof(transactionId));
        RequireGuid(paymentSessionId.Value, nameof(paymentSessionId));
        RequireGuid(paymentDecisionId.Value, nameof(paymentDecisionId));
        RequireGuid(transitionId.Value, nameof(transitionId));
        RequireText(correlationId.Value, nameof(correlationId));

        if (transactionType == TransactionType.Unknown)
        {
            throw new ArgumentException("Transaction type is required.", nameof(transactionType));
        }

        if (transactionStatus == TransactionStatus.Unknown)
        {
            throw new ArgumentException("Transaction status is required.", nameof(transactionStatus));
        }

        if (transactionAmount.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionAmount), "Transaction amount cannot be negative.");
        }

        if (createdUtc == default)
        {
            throw new ArgumentException("Transaction timestamp is required.", nameof(createdUtc));
        }

        var resolvedReference = reference ?? new TransactionReference($"transaction:{transactionId.Value:N}");
        RequireText(resolvedReference.Value, nameof(reference));

        return new PaymentTransaction(
            transactionId,
            transactionType,
            transactionStatus,
            transactionAmount,
            currency ?? throw new ArgumentNullException(nameof(currency)),
            createdUtc,
            new CorrelationId(correlationId.Value.Trim()),
            paymentSessionId,
            paymentDecisionId,
            transitionId,
            metadata ?? new TransactionMetadata(),
            new TransactionReference(resolvedReference.Value.Trim()));
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

public sealed record TransactionResult
{
    public TransactionStatus Status { get; init; }

    public PaymentTransaction? Transaction { get; init; }

    public IReadOnlyCollection<TransactionFailure> Failures { get; init; }

    public TransactionReason Reason { get; init; }

    public TransactionTrace Trace { get; init; }

    public TransactionMetadata Metadata { get; init; }

    public TransactionResult()
    {
        Failures = Array.AsReadOnly(Array.Empty<TransactionFailure>());
        Reason = new TransactionReason(string.Empty);
        Trace = new TransactionTrace();
        Metadata = new TransactionMetadata();
    }

    public TransactionResult(
        TransactionStatus status,
        PaymentTransaction? transaction,
        IReadOnlyCollection<TransactionFailure>? failures,
        TransactionReason reason,
        TransactionTrace trace,
        TransactionMetadata? metadata = null)
    {
        if (status == TransactionStatus.Unknown)
        {
            throw new ArgumentException("Transaction result status is required.", nameof(status));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Transaction reason is required.", nameof(reason));
        }

        Status = status;
        Transaction = transaction;
        Failures = Array.AsReadOnly(failures?.ToArray() ?? Array.Empty<TransactionFailure>());
        Reason = new TransactionReason(reason.Value.Trim());
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Metadata = metadata ?? new TransactionMetadata();
    }
}

public static class PaymentTransactionFactory
{
    public static PaymentTransaction Create(
        TransactionId transactionId,
        TransactionType transactionType,
        TransactionStatus transactionStatus,
        TransactionAmount transactionAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        DecisionId paymentDecisionId,
        TransitionId transitionId,
        TransactionMetadata? metadata = null,
        TransactionReference? reference = null)
    {
        return PaymentTransaction.Create(
            transactionId,
            transactionType,
            transactionStatus,
            transactionAmount,
            currency,
            createdUtc,
            correlationId,
            paymentSessionId,
            paymentDecisionId,
            transitionId,
            metadata,
            reference);
    }
}

public static class TransactionResultFactory
{
    public static TransactionResult Create(
        TransactionStatus status,
        PaymentTransaction? transaction,
        IReadOnlyCollection<TransactionFailure>? failures,
        TransactionReason reason,
        TransactionTrace trace,
        TransactionMetadata? metadata = null)
    {
        return new TransactionResult(status, transaction, failures, reason, trace, metadata);
    }
}

public sealed class PaymentTransactionRuntime
{
    public TransactionResult Produce(
        PaymentDecision decision,
        StateTransition transition,
        TransactionAmount amount,
        Currency currency,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(transition);

        var type = ResolveTransactionType(transition);
        var transactionId = new TransactionId(PaymentRuntimeDeterminism.CreateId(
            "transaction",
            transition.SessionId.Value.ToString("N"),
            decision.DecisionId.Value.ToString("N"),
            transition.TransitionId.Value.ToString("N"),
            type.ToString()));
        var trace = new TransactionTrace(
            new TransactionTraceId($"transaction:{transactionId.Value:N}"),
            [new TransactionReason("TRANSACTION_STARTED")]);
        var prior = priorTransactions?.ToArray() ?? Array.Empty<PaymentTransaction>();
        var failures = new List<TransactionFailure>();

        ValidateDecision(decision, failures);
        ValidateTransition(decision, transition, failures);
        ValidateMoney(amount, currency, failures);
        ValidatePriorTransactions(transition, prior, failures);

        if (type == TransactionType.Unknown)
        {
            failures.Add(TransactionFailure.TransitionNotApplied);
        }

        if (failures.Contains(TransactionFailure.ReplayDetected))
        {
            return CreateResult(TransactionStatus.Replayed, null, failures, "TRANSACTION_REPLAYED", trace, decision, transition);
        }

        if (failures.Contains(TransactionFailure.DuplicateTransaction))
        {
            return CreateResult(TransactionStatus.Duplicate, null, failures, "TRANSACTION_DUPLICATE", trace, decision, transition);
        }

        if (failures.Count > 0)
        {
            return CreateResult(TransactionStatus.Rejected, null, failures, "TRANSACTION_INVALID", trace, decision, transition);
        }

        var reason = ReasonFor(type, transition);
        var metadata = MetadataFor(decision, transition);
        var transaction = PaymentTransactionFactory.Create(
            transactionId,
            type,
            TransactionStatus.Created,
            amount,
            currency,
            transition.Timestamp.Value,
            new CorrelationId($"decision:{decision.DecisionId.Value:N}:transition:{transition.TransitionId.Value:N}"),
            transition.SessionId,
            decision.DecisionId,
            transition.TransitionId,
            metadata);

        return TransactionResultFactory.Create(
            TransactionStatus.Created,
            transaction,
            Array.Empty<TransactionFailure>(),
            new TransactionReason(reason),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new TransactionReason(reason)]).ToArray()) },
            metadata);
    }

    private static void ValidateDecision(PaymentDecision decision, ICollection<TransactionFailure> failures)
    {
        if (decision.DecisionId.Value == Guid.Empty
            || decision.DecisionType == DecisionType.Unknown
            || decision.Result.Status != DecisionStatus.Produced)
        {
            failures.Add(TransactionFailure.DecisionMissing);
        }
    }

    private static void ValidateTransition(
        PaymentDecision decision,
        StateTransition transition,
        ICollection<TransactionFailure> failures)
    {
        if (transition.TransitionId.Value == Guid.Empty
            || transition.SessionId.Value == Guid.Empty
            || transition.DecisionId.Value == Guid.Empty)
        {
            failures.Add(TransactionFailure.IdentifierInvalid);
        }

        if (transition.DecisionId != decision.DecisionId)
        {
            failures.Add(TransactionFailure.OwnershipInvalid);
        }

        if (transition.FromStatus == PaymentStatus.Unknown || transition.ToStatus == PaymentStatus.Unknown)
        {
            failures.Add(TransactionFailure.TransitionNotApplied);
        }

        if (!StateTransitionRuntime.IsLegal(transition.FromStatus, transition.ToStatus)
            || StateTransitionRuntime.ResolveTargetStatus(transition.FromStatus, decision.DecisionType) != transition.ToStatus)
        {
            failures.Add(TransactionFailure.TransitionNotApplied);
        }
    }

    private static void ValidateMoney(TransactionAmount amount, Currency currency, ICollection<TransactionFailure> failures)
    {
        if (amount.Value < 0m)
        {
            failures.Add(TransactionFailure.AmountInvalid);
        }

        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            failures.Add(TransactionFailure.CurrencyInvalid);
        }
    }

    private static void ValidatePriorTransactions(
        StateTransition transition,
        IReadOnlyCollection<PaymentTransaction> prior,
        ICollection<TransactionFailure> failures)
    {
        if (prior.Any(transaction => transaction.PaymentSessionId != transition.SessionId))
        {
            failures.Add(TransactionFailure.OwnershipInvalid);
        }

        if (prior.Any(transaction => transaction.TransitionId == transition.TransitionId))
        {
            failures.Add(TransactionFailure.ReplayDetected);
        }

        var duplicateReferences = prior
            .GroupBy(transaction => transaction.Reference.Value, StringComparer.Ordinal)
            .Any(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
        if (duplicateReferences)
        {
            failures.Add(TransactionFailure.DuplicateTransaction);
        }
    }

    internal static TransactionType ResolveTransactionType(StateTransition transition)
    {
        return transition.ToStatus switch
        {
            PaymentStatus.Approved or PaymentStatus.Authorized => TransactionType.Authorization,
            PaymentStatus.Completed or PaymentStatus.Captured => TransactionType.Sale,
            PaymentStatus.SettlementPending => TransactionType.SettlementReservation,
            PaymentStatus.Settled => TransactionType.Capture,
            PaymentStatus.Refunded => TransactionType.Refund,
            PaymentStatus.PartiallyRefunded => TransactionType.PartialRefund,
            PaymentStatus.Cancelled => TransactionType.Void,
            PaymentStatus.Rejected or PaymentStatus.Expired => TransactionType.Reversal,
            _ => TransactionType.Unknown
        };
    }

    private static string ReasonFor(TransactionType type, StateTransition transition)
    {
        return $"{transition.FromStatus.ToString().ToUpperInvariant()}_TO_{transition.ToStatus.ToString().ToUpperInvariant()}_{type.ToString().ToUpperInvariant()}";
    }

    private static TransactionMetadata MetadataFor(PaymentDecision decision, StateTransition transition)
    {
        return new TransactionMetadata(new Dictionary<string, string>
        {
            ["sessionId"] = transition.SessionId.Value.ToString("D"),
            ["decisionId"] = decision.DecisionId.Value.ToString("D"),
            ["transitionId"] = transition.TransitionId.Value.ToString("D"),
            ["fromStatus"] = transition.FromStatus.ToString(),
            ["toStatus"] = transition.ToStatus.ToString()
        });
    }

    private static TransactionResult CreateResult(
        TransactionStatus status,
        PaymentTransaction? transaction,
        IReadOnlyCollection<TransactionFailure> failures,
        string reason,
        TransactionTrace trace,
        PaymentDecision decision,
        StateTransition transition)
    {
        var metadata = new TransactionMetadata(new Dictionary<string, string>
        {
            ["sessionId"] = transition.SessionId.Value.ToString("D"),
            ["decisionId"] = decision.DecisionId.Value.ToString("D"),
            ["transitionId"] = transition.TransitionId.Value.ToString("D")
        });

        return TransactionResultFactory.Create(
            status,
            transaction,
            failures,
            new TransactionReason(reason),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new TransactionReason(reason)]).ToArray()) },
            metadata);
    }
}
