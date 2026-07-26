namespace PointOfSale.Core.Payments.Runtime.Domain;

public readonly record struct PaymentId(Guid Value);

public readonly record struct PaymentSessionId(Guid Value);

public readonly record struct PaymentAttemptId(Guid Value);

public readonly record struct PaymentActionId(Guid Value);

public readonly record struct EvidenceId(Guid Value);

public readonly record struct DecisionId(Guid Value);

public readonly record struct TransactionId(Guid Value);

public readonly record struct SettlementId(Guid Value);

public readonly record struct ProviderSessionId(string Value);

public readonly record struct PaymentReference(string Value);

public readonly record struct ProviderReference(string Value);

public readonly record struct AttemptNumber(int Value);

public readonly record struct FailureReason(string Value);

public readonly record struct ReasonCode(string Value);

public readonly record struct CorrelationId(string Value);

public readonly record struct ActionCorrelationId(string Value);

public readonly record struct ActionReference(string Value);

public readonly record struct EvidenceReference(string Value);

public readonly record struct EvidenceHash(string Value);

public readonly record struct EvidenceSource(string Value);

public readonly record struct EvidenceTimestamp(DateTimeOffset Value);

public readonly record struct ProviderPayloadReference(string Value);
