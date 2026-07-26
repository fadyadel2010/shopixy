using PointOfSale.Core.Payments.Runtime.Domain;
using DomainAdmissionResult = PointOfSale.Core.Payments.Runtime.Domain.AdmissionResult;
using DomainDecisionResult = PointOfSale.Core.Payments.Runtime.Domain.DecisionResult;
using DomainSettlementResult = PointOfSale.Core.Payments.Runtime.Domain.SettlementResult;
using DomainTransactionResult = PointOfSale.Core.Payments.Runtime.Domain.TransactionResult;
using DomainTransitionResult = PointOfSale.Core.Payments.Runtime.Domain.TransitionResult;
using DomainVerificationResult = PointOfSale.Core.Payments.Runtime.Domain.VerificationResult;

namespace PointOfSale.Core.Payments.Runtime.Results;

public sealed record PaymentDecisionResult(
    DecisionId DecisionId,
    DecisionType DecisionType,
    PaymentStatus ResultingStatus,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record VerificationResult(
    EvidenceId EvidenceId,
    bool Accepted,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record EvidenceAdmissionResult(
    EvidenceId EvidenceId,
    EvidenceType EvidenceType,
    bool Admitted,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record SettlementResult(
    SettlementId SettlementId,
    SettlementStatus Status,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record PolicyResult(
    DecisionType DecisionType,
    IReadOnlyCollection<string> ReasonCodes);

public sealed record RuntimeResult
{
    public DomainAdmissionResult? AdmissionResult { get; init; }

    public DomainVerificationResult? VerificationResult { get; init; }

    public DomainDecisionResult? DecisionResult { get; init; }

    public DomainTransitionResult? TransitionResult { get; init; }

    public DomainTransactionResult? TransactionResult { get; init; }

    public DomainSettlementResult? SettlementResult { get; init; }

    public RuntimeOverallStatus OverallStatus { get; init; }

    public RuntimeTrace ExecutionTrace { get; init; }

    public RuntimeFailure? Failure { get; init; }

    public RuntimeExecutionContext ExecutionContext { get; init; }

    public AdmittedEvidence? AdmittedEvidence { get; init; }

    public VerifiedEvidence? VerifiedEvidence { get; init; }

    public PaymentDecision? PaymentDecision { get; init; }

    public StateTransition? StateTransition { get; init; }

    public PaymentTransaction? PaymentTransaction { get; init; }

    public PaymentSettlement? PaymentSettlement { get; init; }

    public PaymentSessionId SessionId => ExecutionContext.PaymentSessionId;

    public PaymentStatus Status => TransitionResult?.Status == TransitionStatus.Applied
        ? TransitionResult.Session.Status
        : OverallStatus == RuntimeOverallStatus.Failed
            ? PaymentStatus.Failed
            : PaymentStatus.Unknown;

    public IReadOnlyCollection<string> ReasonCodes => Failure is null
        ? Array.Empty<string>()
        : new[] { Failure.FailureReason.Value };

    public RuntimeResult()
    {
        ExecutionTrace = new RuntimeTrace();
        ExecutionContext = new RuntimeExecutionContext();
    }

    public RuntimeResult(
        RuntimeExecutionContext executionContext,
        RuntimeOverallStatus overallStatus,
        RuntimeTrace executionTrace,
        DomainAdmissionResult? admissionResult = null,
        DomainVerificationResult? verificationResult = null,
        DomainDecisionResult? decisionResult = null,
        DomainTransitionResult? transitionResult = null,
        DomainTransactionResult? transactionResult = null,
        DomainSettlementResult? settlementResult = null,
        RuntimeFailure? failure = null,
        AdmittedEvidence? admittedEvidence = null,
        VerifiedEvidence? verifiedEvidence = null,
        PaymentDecision? paymentDecision = null,
        StateTransition? stateTransition = null,
        PaymentTransaction? paymentTransaction = null,
        PaymentSettlement? paymentSettlement = null)
    {
        if (overallStatus == RuntimeOverallStatus.Unknown)
        {
            throw new ArgumentException("Runtime status is required.", nameof(overallStatus));
        }

        ExecutionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
        OverallStatus = overallStatus;
        ExecutionTrace = executionTrace ?? throw new ArgumentNullException(nameof(executionTrace));
        AdmissionResult = admissionResult;
        VerificationResult = verificationResult;
        DecisionResult = decisionResult;
        TransitionResult = transitionResult;
        TransactionResult = transactionResult;
        SettlementResult = settlementResult;
        Failure = failure;
        AdmittedEvidence = admittedEvidence;
        VerifiedEvidence = verifiedEvidence;
        PaymentDecision = paymentDecision;
        StateTransition = stateTransition;
        PaymentTransaction = paymentTransaction;
        PaymentSettlement = paymentSettlement;
    }
}
