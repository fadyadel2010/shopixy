using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Core.Payments.Runtime.Contracts;

public interface IPaymentRuntime
{
}

public interface IPaymentProvider
{
}

public interface IPaymentSession
{
    PaymentSessionId Id { get; }

    PaymentId PaymentId { get; }

    PaymentStatus Status { get; }
}

public interface IPaymentAttempt
{
    PaymentAttemptId Id { get; }

    PaymentSessionId SessionId { get; }
}

public interface IPaymentEvidence
{
    EvidenceId Id { get; }

    EvidenceType Type { get; }
}

public interface IPaymentVerification
{
    EvidenceId EvidenceId { get; }

    bool Accepted { get; }
}

public interface IPaymentDecision
{
    DecisionId Id { get; }

    DecisionType Type { get; }
}

public interface IPaymentTransaction
{
    TransactionId Id { get; }

    PaymentSessionId SessionId { get; }
}

public interface IPaymentSettlement
{
    SettlementId Id { get; }

    SettlementStatus Status { get; }
}

public interface IPaymentPolicyEngine
{
}

public interface IPolicyContext
{
}

public interface IPipelineContext
{
}
