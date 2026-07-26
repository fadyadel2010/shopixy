using System.Collections.ObjectModel;
using PointOfSale.Core.Payments.Runtime.Contracts;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum SettlementType
{
    Unknown = 0,
    AuthorizationSettlement = 1,
    CaptureSettlement = 2,
    SaleSettlement = 3,
    RefundSettlement = 4,
    ChargebackSettlement = 5,
    AdjustmentSettlement = 6,
    ReservationSettlement = 7,
    ReversalSettlement = 8,
    ManualSettlement = 9,
    OfflineCollectionSettlement = 10
}

public enum SettlementFailure
{
    None = 0,
    TransactionMissing = 1,
    IdentifierInvalid = 2,
    AmountInvalid = 3,
    CurrencyInvalid = 4,
    OwnershipInvalid = 5,
    ReplayDetected = 6,
    DuplicateSettlement = 7,
    ReferenceInvalid = 8,
    TransactionInvalid = 9
}

public readonly record struct SettlementReference(string Value);

public readonly record struct SettlementAmount(decimal Value);

public readonly record struct SettlementTimestamp(DateTimeOffset Value);

public readonly record struct SettlementReason(string Value);

public readonly record struct SettlementTraceId(string Value);

public sealed record SettlementMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public SettlementMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record SettlementTrace
{
    public SettlementTraceId Id { get; init; }

    public IReadOnlyCollection<SettlementReason> Reasons { get; init; }

    public SettlementTrace()
    {
        Id = new SettlementTraceId(string.Empty);
        Reasons = Array.AsReadOnly(Array.Empty<SettlementReason>());
    }

    public SettlementTrace(SettlementTraceId id, IReadOnlyCollection<SettlementReason>? reasons = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Settlement trace identifier is required.", nameof(id));
        }

        Id = new SettlementTraceId(id.Value.Trim());
        Reasons = Array.AsReadOnly(reasons?.ToArray() ?? Array.Empty<SettlementReason>());
    }
}

public sealed record PaymentSettlement : IPaymentSettlement
{
    public SettlementId SettlementId { get; init; }

    public SettlementType SettlementType { get; init; }

    public SettlementStatus SettlementStatus { get; init; }

    public SettlementAmount SettlementAmount { get; init; }

    public Currency Currency { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public CorrelationId CorrelationId { get; init; }

    public PaymentSessionId PaymentSessionId { get; init; }

    public TransactionId PaymentTransactionId { get; init; }

    public SettlementReference SettlementReference { get; init; }

    public SettlementMetadata Metadata { get; init; }

    public SettlementId Id => SettlementId;

    public SettlementStatus Status => SettlementStatus;

    public PaymentSettlement()
    {
        Currency = new Currency("EGP");
        CorrelationId = new CorrelationId(string.Empty);
        SettlementReference = new SettlementReference(string.Empty);
        Metadata = new SettlementMetadata();
    }

    private PaymentSettlement(
        SettlementId settlementId,
        SettlementType settlementType,
        SettlementStatus settlementStatus,
        SettlementAmount settlementAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        TransactionId paymentTransactionId,
        SettlementReference settlementReference,
        SettlementMetadata metadata)
    {
        SettlementId = settlementId;
        SettlementType = settlementType;
        SettlementStatus = settlementStatus;
        SettlementAmount = settlementAmount;
        Currency = currency;
        CreatedUtc = createdUtc;
        CorrelationId = correlationId;
        PaymentSessionId = paymentSessionId;
        PaymentTransactionId = paymentTransactionId;
        SettlementReference = settlementReference;
        Metadata = metadata;
    }

    public static PaymentSettlement Create(
        SettlementId settlementId,
        SettlementType settlementType,
        SettlementStatus settlementStatus,
        SettlementAmount settlementAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        TransactionId paymentTransactionId,
        SettlementReference settlementReference,
        SettlementMetadata? metadata = null)
    {
        RequireGuid(settlementId.Value, nameof(settlementId));
        RequireGuid(paymentSessionId.Value, nameof(paymentSessionId));
        RequireGuid(paymentTransactionId.Value, nameof(paymentTransactionId));
        RequireText(correlationId.Value, nameof(correlationId));
        RequireText(settlementReference.Value, nameof(settlementReference));

        if (settlementType == SettlementType.Unknown)
        {
            throw new ArgumentException("Settlement type is required.", nameof(settlementType));
        }

        if (settlementStatus == SettlementStatus.Unknown)
        {
            throw new ArgumentException("Settlement status is required.", nameof(settlementStatus));
        }

        if (settlementAmount.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(settlementAmount), "Settlement amount cannot be negative.");
        }

        if (createdUtc == default)
        {
            throw new ArgumentException("Settlement timestamp is required.", nameof(createdUtc));
        }

        return new PaymentSettlement(
            settlementId,
            settlementType,
            settlementStatus,
            settlementAmount,
            currency ?? throw new ArgumentNullException(nameof(currency)),
            createdUtc,
            new CorrelationId(correlationId.Value.Trim()),
            paymentSessionId,
            paymentTransactionId,
            new SettlementReference(settlementReference.Value.Trim()),
            metadata ?? new SettlementMetadata());
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

public sealed record SettlementResult
{
    public SettlementStatus Status { get; init; }

    public PaymentSettlement? Settlement { get; init; }

    public IReadOnlyCollection<SettlementFailure> Failures { get; init; }

    public SettlementReason Reason { get; init; }

    public SettlementTrace Trace { get; init; }

    public SettlementMetadata Metadata { get; init; }

    public SettlementResult()
    {
        Failures = Array.AsReadOnly(Array.Empty<SettlementFailure>());
        Reason = new SettlementReason(string.Empty);
        Trace = new SettlementTrace();
        Metadata = new SettlementMetadata();
    }

    public SettlementResult(
        SettlementStatus status,
        PaymentSettlement? settlement,
        IReadOnlyCollection<SettlementFailure>? failures,
        SettlementReason reason,
        SettlementTrace trace,
        SettlementMetadata? metadata = null)
    {
        if (status == SettlementStatus.Unknown)
        {
            throw new ArgumentException("Settlement result status is required.", nameof(status));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Settlement reason is required.", nameof(reason));
        }

        Status = status;
        Settlement = settlement;
        Failures = Array.AsReadOnly(failures?.ToArray() ?? Array.Empty<SettlementFailure>());
        Reason = new SettlementReason(reason.Value.Trim());
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Metadata = metadata ?? new SettlementMetadata();
    }
}

public static class PaymentSettlementFactory
{
    public static PaymentSettlement Create(
        SettlementId settlementId,
        SettlementType settlementType,
        SettlementStatus settlementStatus,
        SettlementAmount settlementAmount,
        Currency currency,
        DateTimeOffset createdUtc,
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        TransactionId paymentTransactionId,
        SettlementReference settlementReference,
        SettlementMetadata? metadata = null)
    {
        return PaymentSettlement.Create(
            settlementId,
            settlementType,
            settlementStatus,
            settlementAmount,
            currency,
            createdUtc,
            correlationId,
            paymentSessionId,
            paymentTransactionId,
            settlementReference,
            metadata);
    }
}

public static class SettlementResultFactory
{
    public static SettlementResult Create(
        SettlementStatus status,
        PaymentSettlement? settlement,
        IReadOnlyCollection<SettlementFailure>? failures,
        SettlementReason reason,
        SettlementTrace trace,
        SettlementMetadata? metadata = null)
    {
        return new SettlementResult(status, settlement, failures, reason, trace, metadata);
    }
}

public sealed class PaymentSettlementRuntime
{
    public SettlementResult Produce(
        PaymentTransaction transaction,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var settlementId = DeterministicSettlementId(transaction);
        var trace = new SettlementTrace(
            new SettlementTraceId($"settlement:{settlementId.Value:N}"),
            [new SettlementReason("SETTLEMENT_STARTED")]);
        var prior = priorSettlements?.ToArray() ?? Array.Empty<PaymentSettlement>();
        var failures = new List<SettlementFailure>();

        ValidateTransaction(transaction, failures);
        ValidatePriorSettlements(transaction, settlementId, prior, failures);

        if (failures.Contains(SettlementFailure.ReplayDetected))
        {
            return CreateResult(SettlementStatus.Replayed, null, failures, "SETTLEMENT_REPLAYED", trace, transaction);
        }

        if (failures.Contains(SettlementFailure.DuplicateSettlement))
        {
            return CreateResult(SettlementStatus.Duplicate, null, failures, "SETTLEMENT_DUPLICATE", trace, transaction);
        }

        if (failures.Count > 0)
        {
            return CreateResult(SettlementStatus.Rejected, null, failures, "SETTLEMENT_INVALID", trace, transaction);
        }

        var type = ResolveSettlementType(transaction);
        var reason = ReasonFor(type, transaction);
        var metadata = MetadataFor(transaction);
        var settlement = PaymentSettlementFactory.Create(
            settlementId,
            type,
            SettlementStatus.Pending,
            new SettlementAmount(transaction.TransactionAmount.Value),
            transaction.Currency,
            transaction.CreatedUtc,
            new CorrelationId($"transaction:{transaction.TransactionId.Value:N}"),
            transaction.PaymentSessionId,
            transaction.TransactionId,
            ReferenceFor(settlementId),
            metadata);

        return SettlementResultFactory.Create(
            SettlementStatus.Pending,
            settlement,
            Array.Empty<SettlementFailure>(),
            new SettlementReason(reason),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new SettlementReason(reason)]).ToArray()) },
            metadata);
    }

    private static void ValidateTransaction(PaymentTransaction transaction, ICollection<SettlementFailure> failures)
    {
        if (transaction.TransactionId.Value == Guid.Empty || transaction.PaymentSessionId.Value == Guid.Empty)
        {
            failures.Add(SettlementFailure.IdentifierInvalid);
        }

        if (transaction.TransactionType == TransactionType.Unknown || transaction.TransactionStatus != TransactionStatus.Created)
        {
            failures.Add(SettlementFailure.TransactionInvalid);
        }

        if (transaction.TransactionAmount.Value < 0m)
        {
            failures.Add(SettlementFailure.AmountInvalid);
        }

        if (transaction.Currency is null || string.IsNullOrWhiteSpace(transaction.Currency.Code))
        {
            failures.Add(SettlementFailure.CurrencyInvalid);
        }

        if (string.IsNullOrWhiteSpace(transaction.Reference.Value))
        {
            failures.Add(SettlementFailure.ReferenceInvalid);
        }
    }

    private static void ValidatePriorSettlements(
        PaymentTransaction transaction,
        SettlementId settlementId,
        IReadOnlyCollection<PaymentSettlement> prior,
        ICollection<SettlementFailure> failures)
    {
        if (prior.Any(settlement => settlement.PaymentSessionId != transaction.PaymentSessionId))
        {
            failures.Add(SettlementFailure.OwnershipInvalid);
        }

        if (prior.Any(settlement => settlement.PaymentTransactionId == transaction.TransactionId
            || settlement.SettlementId == settlementId))
        {
            failures.Add(SettlementFailure.ReplayDetected);
        }

        var duplicateReferences = prior
            .GroupBy(settlement => settlement.SettlementReference.Value, StringComparer.Ordinal)
            .Any(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
        if (duplicateReferences)
        {
            failures.Add(SettlementFailure.DuplicateSettlement);
        }
    }

    internal static SettlementType ResolveSettlementType(PaymentTransaction transaction)
    {
        if (transaction.Metadata.Values.TryGetValue("settlementType", out var explicitType)
            && Enum.TryParse<SettlementType>(explicitType, ignoreCase: false, out var parsed)
            && parsed != SettlementType.Unknown)
        {
            return parsed;
        }

        return transaction.TransactionType switch
        {
            TransactionType.Authorization => SettlementType.AuthorizationSettlement,
            TransactionType.Capture => SettlementType.CaptureSettlement,
            TransactionType.Sale => SettlementType.SaleSettlement,
            TransactionType.Refund or TransactionType.PartialRefund => SettlementType.RefundSettlement,
            TransactionType.Chargeback => SettlementType.ChargebackSettlement,
            TransactionType.Adjustment => SettlementType.AdjustmentSettlement,
            TransactionType.SettlementReservation => SettlementType.ReservationSettlement,
            TransactionType.Void or TransactionType.Reversal => SettlementType.ReversalSettlement,
            _ => SettlementType.AdjustmentSettlement
        };
    }

    private static string ReasonFor(SettlementType type, PaymentTransaction transaction)
    {
        return $"{transaction.TransactionType.ToString().ToUpperInvariant()}_TO_{type.ToString().ToUpperInvariant()}";
    }

    private static SettlementMetadata MetadataFor(PaymentTransaction transaction)
    {
        return new SettlementMetadata(new Dictionary<string, string>
        {
            ["sessionId"] = transaction.PaymentSessionId.Value.ToString("D"),
            ["transactionId"] = transaction.TransactionId.Value.ToString("D"),
            ["transactionType"] = transaction.TransactionType.ToString(),
            ["transactionStatus"] = transaction.TransactionStatus.ToString()
        });
    }

    private static SettlementResult CreateResult(
        SettlementStatus status,
        PaymentSettlement? settlement,
        IReadOnlyCollection<SettlementFailure> failures,
        string reason,
        SettlementTrace trace,
        PaymentTransaction transaction)
    {
        var metadata = MetadataFor(transaction);

        return SettlementResultFactory.Create(
            status,
            settlement,
            failures,
            new SettlementReason(reason),
            trace with { Reasons = Array.AsReadOnly(trace.Reasons.Concat([new SettlementReason(reason)]).ToArray()) },
            metadata);
    }

    private static SettlementId DeterministicSettlementId(PaymentTransaction transaction)
    {
        return new SettlementId(PaymentRuntimeDeterminism.CreateId(
            "payment-settlement",
            transaction.TransactionId.Value.ToString("N"),
            transaction.TransactionType.ToString()));
    }

    private static SettlementReference ReferenceFor(SettlementId settlementId)
    {
        return new SettlementReference($"settlement:{settlementId.Value:N}");
    }
}
