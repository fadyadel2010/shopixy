using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Results;
using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public enum PipelineStage
{
    Unknown = 0,
    ProviderEvidence = 1,
    EvidenceAdmissionRuntime = 2,
    EvidenceVerificationRuntime = 3,
    PaymentDecisionRuntime = 4,
    StateTransitionRuntime = 5,
    PaymentTransactionRuntime = 6,
    PaymentSettlementRuntime = 7,
    RuntimeResult = 8
}

public enum RuntimeOverallStatus
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2
}

public enum FailureStage
{
    Unknown = 0,
    ProviderEvidence = 1,
    Admission = 2,
    Verification = 3,
    Decision = 4,
    Transition = 5,
    Transaction = 6,
    Settlement = 7
}

public readonly record struct ExecutionId(Guid Value);

public readonly record struct ExecutionTraceId(string Value);

public readonly record struct ExecutionReference(string Value);

public readonly record struct RuntimeFailureReason(string Value);

public readonly record struct RuntimeTraceReason(string Value);

public sealed record PipelineMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public PipelineMetadata(IReadOnlyDictionary<string, string>? values = null)
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

public sealed record RuntimeTraceEntry
{
    public PipelineStage Stage { get; init; }

    public RuntimeTraceReason Reason { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public RuntimeTraceEntry()
    {
        Reason = new RuntimeTraceReason(string.Empty);
    }

    public RuntimeTraceEntry(PipelineStage stage, RuntimeTraceReason reason, DateTimeOffset timestamp)
    {
        if (stage == PipelineStage.Unknown)
        {
            throw new ArgumentException("Pipeline stage is required.", nameof(stage));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Runtime trace reason is required.", nameof(reason));
        }

        if (timestamp == default)
        {
            throw new ArgumentException("Runtime trace timestamp is required.", nameof(timestamp));
        }

        Stage = stage;
        Reason = new RuntimeTraceReason(reason.Value.Trim());
        Timestamp = timestamp;
    }
}

public sealed record RuntimeTrace
{
    public ExecutionTraceId Id { get; init; }

    public IReadOnlyCollection<RuntimeTraceEntry> Entries { get; init; }

    public RuntimeTrace()
    {
        Id = new ExecutionTraceId(string.Empty);
        Entries = Array.AsReadOnly(Array.Empty<RuntimeTraceEntry>());
    }

    public RuntimeTrace(ExecutionTraceId id, IReadOnlyCollection<RuntimeTraceEntry>? entries = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Execution trace identifier is required.", nameof(id));
        }

        Id = new ExecutionTraceId(id.Value.Trim());
        Entries = Array.AsReadOnly(entries?.ToArray() ?? Array.Empty<RuntimeTraceEntry>());
    }
}

public sealed record RuntimeExecutionContext
{
    public CorrelationId CorrelationId { get; init; }

    public PaymentSessionId PaymentSessionId { get; init; }

    public ExecutionId ExecutionId { get; init; }

    public PipelineMetadata PipelineMetadata { get; init; }

    public RuntimeTrace RuntimeTrace { get; init; }

    public DateTimeOffset ExecutionTimestamp { get; init; }

    public RuntimeExecutionContext()
    {
        CorrelationId = new CorrelationId(string.Empty);
        PipelineMetadata = new PipelineMetadata();
        RuntimeTrace = new RuntimeTrace();
    }

    public RuntimeExecutionContext(
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        ExecutionId executionId,
        PipelineMetadata? pipelineMetadata,
        RuntimeTrace runtimeTrace,
        DateTimeOffset executionTimestamp)
    {
        RequireText(correlationId.Value, nameof(correlationId));
        RequireGuid(paymentSessionId.Value, nameof(paymentSessionId));
        RequireGuid(executionId.Value, nameof(executionId));

        if (executionTimestamp == default)
        {
            throw new ArgumentException("Execution timestamp is required.", nameof(executionTimestamp));
        }

        CorrelationId = new CorrelationId(correlationId.Value.Trim());
        PaymentSessionId = paymentSessionId;
        ExecutionId = executionId;
        PipelineMetadata = pipelineMetadata ?? new PipelineMetadata();
        RuntimeTrace = runtimeTrace ?? throw new ArgumentNullException(nameof(runtimeTrace));
        ExecutionTimestamp = executionTimestamp;
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

public sealed record RuntimeFailure
{
    public FailureStage FailureStage { get; init; }

    public RuntimeFailureReason FailureReason { get; init; }

    public RuntimeTrace FailureTrace { get; init; }

    public PipelineMetadata FailureMetadata { get; init; }

    public RuntimeFailure()
    {
        FailureReason = new RuntimeFailureReason(string.Empty);
        FailureTrace = new RuntimeTrace();
        FailureMetadata = new PipelineMetadata();
    }

    public RuntimeFailure(
        FailureStage failureStage,
        RuntimeFailureReason failureReason,
        RuntimeTrace failureTrace,
        PipelineMetadata? failureMetadata = null)
    {
        if (failureStage == FailureStage.Unknown)
        {
            throw new ArgumentException("Failure stage is required.", nameof(failureStage));
        }

        if (string.IsNullOrWhiteSpace(failureReason.Value))
        {
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));
        }

        FailureStage = failureStage;
        FailureReason = new RuntimeFailureReason(failureReason.Value.Trim());
        FailureTrace = failureTrace ?? throw new ArgumentNullException(nameof(failureTrace));
        FailureMetadata = failureMetadata ?? new PipelineMetadata();
    }
}

public interface IEvidenceAdmissionStage
{
    AdmissionResult Admit(ProviderEvidence evidence, PaymentAction action);
}

public interface IEvidenceVerificationStage
{
    VerifiedEvidence Verify(AdmittedEvidence evidence, EvidenceVerificationContext context);
}

public interface IPaymentDecisionStage
{
    PaymentDecision Decide(VerifiedEvidence evidence);
}

public interface IStateTransitionStage
{
    TransitionResult Transition(
        PaymentDecision decision,
        PaymentSession session,
        IReadOnlyCollection<StateTransition>? priorTransitions = null);
}

public interface IPaymentTransactionStage
{
    TransactionResult Produce(
        PaymentDecision decision,
        StateTransition transition,
        TransactionAmount amount,
        Currency currency,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null);
}

public interface IPaymentSettlementStage
{
    SettlementResult Produce(
        PaymentTransaction transaction,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null);
}

public sealed class PaymentRuntimeOrchestrator : IPaymentRuntime
{
    private readonly IEvidenceAdmissionStage admissionStage;
    private readonly IEvidenceVerificationStage verificationStage;
    private readonly IPaymentDecisionStage decisionStage;
    private readonly IStateTransitionStage transitionStage;
    private readonly IPaymentTransactionStage transactionStage;
    private readonly IPaymentSettlementStage settlementStage;

    public PaymentRuntimeOrchestrator()
        : this(
            new EvidenceAdmissionStage(),
            new EvidenceVerificationStage(),
            new PaymentDecisionStage(),
            new StateTransitionStage(),
            new PaymentTransactionStage(),
            new PaymentSettlementStage())
    {
    }

    public PaymentRuntimeOrchestrator(
        IEvidenceAdmissionStage admissionStage,
        IEvidenceVerificationStage verificationStage,
        IPaymentDecisionStage decisionStage,
        IStateTransitionStage transitionStage,
        IPaymentTransactionStage transactionStage,
        IPaymentSettlementStage settlementStage)
    {
        this.admissionStage = admissionStage ?? throw new ArgumentNullException(nameof(admissionStage));
        this.verificationStage = verificationStage ?? throw new ArgumentNullException(nameof(verificationStage));
        this.decisionStage = decisionStage ?? throw new ArgumentNullException(nameof(decisionStage));
        this.transitionStage = transitionStage ?? throw new ArgumentNullException(nameof(transitionStage));
        this.transactionStage = transactionStage ?? throw new ArgumentNullException(nameof(transactionStage));
        this.settlementStage = settlementStage ?? throw new ArgumentNullException(nameof(settlementStage));
    }

    public RuntimeResult Execute(
        RuntimeExecutionContext context,
        ProviderEvidence evidence,
        PaymentAction action,
        PaymentSession session,
        IReadOnlyCollection<AdmittedEvidence>? priorEvidence = null,
        IReadOnlyCollection<StateTransition>? priorTransitions = null,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(session);

        var trace = Append(context.RuntimeTrace, PipelineStage.ProviderEvidence, "PROVIDER_EVIDENCE_RECEIVED", context.ExecutionTimestamp);
        AdmissionResult? admission = null;
        VerifiedEvidence? verifiedEvidence = null;
        PaymentDecision? decision = null;
        TransitionResult? transition = null;
        TransactionResult? transaction = null;
        SettlementResult? settlement = null;

        try
        {
            if (context.PaymentSessionId != session.Id || action.SessionId != session.Id)
            {
                return Fail(context, trace, FailureStage.ProviderEvidence, "SESSION_LINEAGE_INVALID");
            }

            if (session.Attempts.Count > 0
                && !session.Attempts.Any(attempt => attempt.Id == action.AttemptId && attempt.SessionId == session.Id))
            {
                return Fail(context, trace, FailureStage.ProviderEvidence, "ATTEMPT_LINEAGE_INVALID");
            }

            trace = Append(trace, PipelineStage.EvidenceAdmissionRuntime, "ADMISSION_STARTED", context.ExecutionTimestamp);
            admission = admissionStage.Admit(evidence, action);
            if (!admission.Admitted || admission.Evidence is null)
            {
                return Fail(context, trace, FailureStage.Admission, "ADMISSION_FAILED", admissionResult: admission);
            }

            if (admission.Evidence.EvidenceId != evidence.Id
                || admission.Evidence.ActionId != action.Id
                || admission.Evidence.CorrelationId != action.CorrelationId)
            {
                return Fail(context, trace, FailureStage.Admission, "ADMISSION_LINEAGE_INVALID", admissionResult: admission);
            }

            trace = Append(trace, PipelineStage.EvidenceVerificationRuntime, "VERIFICATION_STARTED", context.ExecutionTimestamp);
            verifiedEvidence = verificationStage.Verify(
                admission.Evidence,
                new EvidenceVerificationContext(action, priorEvidence));
            if (!verifiedEvidence.VerificationResult.Accepted)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Verification,
                    "VERIFICATION_FAILED",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence);
            }

            if (verifiedEvidence.EvidenceId != admission.Evidence.EvidenceId
                || verifiedEvidence.VerificationResult.EvidenceId != admission.Evidence.EvidenceId
                || verifiedEvidence.VerificationResult.VerificationId != verifiedEvidence.VerificationId)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Verification,
                    "VERIFICATION_LINEAGE_INVALID",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence);
            }

            trace = Append(trace, PipelineStage.PaymentDecisionRuntime, "DECISION_STARTED", context.ExecutionTimestamp);
            decision = decisionStage.Decide(verifiedEvidence);
            if (decision.Result.Status != DecisionStatus.Produced)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Decision,
                    "DECISION_FAILED",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision);
            }

            if (decision.VerifiedEvidenceId.Value != verifiedEvidence.VerificationId.Value)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Decision,
                    "DECISION_LINEAGE_INVALID",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision);
            }

            trace = Append(trace, PipelineStage.StateTransitionRuntime, "TRANSITION_STARTED", context.ExecutionTimestamp);
            transition = transitionStage.Transition(decision, session, priorTransitions);
            if (transition.Status != TransitionStatus.Applied || transition.Transition is null)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Transition,
                    "TRANSITION_FAILED",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision);
            }

            if (transition.Session.Id != session.Id
                || transition.Transition.SessionId != session.Id
                || transition.Transition.DecisionId != decision.DecisionId
                || transition.Transition.FromStatus != session.Status
                || transition.Transition.ToStatus != transition.Session.Status
                || !StateTransitionRuntime.IsLegal(transition.Transition.FromStatus, transition.Transition.ToStatus)
                || StateTransitionRuntime.ResolveTargetStatus(transition.Transition.FromStatus, decision.DecisionType)
                    != transition.Transition.ToStatus)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Transition,
                    "TRANSITION_LINEAGE_INVALID",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision);
            }

            trace = Append(trace, PipelineStage.PaymentTransactionRuntime, "TRANSACTION_STARTED", context.ExecutionTimestamp);
            transaction = transactionStage.Produce(
                decision,
                transition.Transition,
                new TransactionAmount(session.Amount.Amount),
                session.Amount.Currency,
                priorTransactions);
            if (transaction.Status != TransactionStatus.Created || transaction.Transaction is null)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Transaction,
                    "TRANSACTION_FAILED",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    transactionResult: transaction,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision,
                    stateTransition: transition.Transition);
            }

            if (transaction.Transaction.PaymentSessionId != session.Id
                || transaction.Transaction.PaymentDecisionId != decision.DecisionId
                || transaction.Transaction.TransitionId != transition.Transition.TransitionId
                || transaction.Transaction.TransactionStatus != TransactionStatus.Created
                || transaction.Transaction.TransactionType != PaymentTransactionRuntime.ResolveTransactionType(transition.Transition)
                || transaction.Transaction.TransactionAmount.Value != session.Amount.Amount
                || transaction.Transaction.Currency.Code != session.Amount.Currency.Code)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Transaction,
                    "TRANSACTION_LINEAGE_INVALID",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    transactionResult: transaction,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision,
                    stateTransition: transition.Transition);
            }

            trace = Append(trace, PipelineStage.PaymentSettlementRuntime, "SETTLEMENT_STARTED", context.ExecutionTimestamp);
            settlement = settlementStage.Produce(transaction.Transaction, priorSettlements);
            if (settlement.Status != SettlementStatus.Pending || settlement.Settlement is null)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Settlement,
                    "SETTLEMENT_FAILED",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    transactionResult: transaction,
                    settlementResult: settlement,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision,
                    stateTransition: transition.Transition,
                    paymentTransaction: transaction.Transaction);
            }

            if (settlement.Settlement.PaymentSessionId != session.Id
                || settlement.Settlement.PaymentTransactionId != transaction.Transaction.TransactionId
                || settlement.Settlement.SettlementStatus != SettlementStatus.Pending
                || settlement.Settlement.SettlementType != PaymentSettlementRuntime.ResolveSettlementType(transaction.Transaction)
                || settlement.Settlement.SettlementAmount.Value != transaction.Transaction.TransactionAmount.Value
                || settlement.Settlement.Currency.Code != transaction.Transaction.Currency.Code)
            {
                return Fail(
                    context,
                    trace,
                    FailureStage.Settlement,
                    "SETTLEMENT_LINEAGE_INVALID",
                    admissionResult: admission,
                    verificationResult: verifiedEvidence.VerificationResult,
                    decisionResult: decision.Result,
                    transitionResult: transition,
                    transactionResult: transaction,
                    settlementResult: settlement,
                    admittedEvidence: admission.Evidence,
                    verifiedEvidence: verifiedEvidence,
                    paymentDecision: decision,
                    stateTransition: transition.Transition,
                    paymentTransaction: transaction.Transaction);
            }

            trace = Append(trace, PipelineStage.RuntimeResult, "RUNTIME_SUCCEEDED", context.ExecutionTimestamp);
            return RuntimeResultFactory.Succeeded(
                context,
                trace,
                admission,
                verifiedEvidence.VerificationResult,
                decision.Result,
                transition,
                transaction,
                settlement,
                admission.Evidence,
                verifiedEvidence,
                decision,
                transition.Transition,
                transaction.Transaction,
                settlement.Settlement);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var failureStage = CurrentFailureStage(admission, verifiedEvidence, decision, transition, transaction, settlement);
            return Fail(
                context,
                trace,
                failureStage,
                exception.GetType().Name,
                admission,
                verifiedEvidence?.VerificationResult,
                decision?.Result,
                transition,
                transaction,
                settlement,
                admission?.Evidence,
                verifiedEvidence,
                decision,
                transition?.Transition,
                transaction?.Transaction);
        }
    }

    private static FailureStage CurrentFailureStage(
        AdmissionResult? admission,
        VerifiedEvidence? verifiedEvidence,
        PaymentDecision? decision,
        TransitionResult? transition,
        TransactionResult? transaction,
        SettlementResult? settlement)
    {
        if (admission is null)
        {
            return FailureStage.Admission;
        }

        if (verifiedEvidence is null)
        {
            return FailureStage.Verification;
        }

        if (decision is null)
        {
            return FailureStage.Decision;
        }

        if (transition is null)
        {
            return FailureStage.Transition;
        }

        if (transaction is null)
        {
            return FailureStage.Transaction;
        }

        if (settlement is null)
        {
            return FailureStage.Settlement;
        }

        return FailureStage.Settlement;
    }

    private static RuntimeResult Fail(
        RuntimeExecutionContext context,
        RuntimeTrace trace,
        FailureStage stage,
        string reason,
        AdmissionResult? admissionResult = null,
        VerificationResult? verificationResult = null,
        DecisionResult? decisionResult = null,
        TransitionResult? transitionResult = null,
        TransactionResult? transactionResult = null,
        SettlementResult? settlementResult = null,
        AdmittedEvidence? admittedEvidence = null,
        VerifiedEvidence? verifiedEvidence = null,
        PaymentDecision? paymentDecision = null,
        StateTransition? stateTransition = null,
        PaymentTransaction? paymentTransaction = null)
    {
        var failureTrace = Append(trace, PipelineStage.RuntimeResult, "RUNTIME_FAILED", context.ExecutionTimestamp);
        var failure = new RuntimeFailure(
            stage,
            new RuntimeFailureReason(reason),
            failureTrace,
            new PipelineMetadata(new Dictionary<string, string>
            {
                ["executionId"] = context.ExecutionId.Value.ToString("D"),
                ["sessionId"] = context.PaymentSessionId.Value.ToString("D")
            }));

        return RuntimeResultFactory.Failed(
            context,
            failureTrace,
            failure,
            admissionResult,
            verificationResult,
            decisionResult,
            transitionResult,
            transactionResult,
            settlementResult,
            admittedEvidence,
            verifiedEvidence,
            paymentDecision,
            stateTransition,
            paymentTransaction);
    }

    private static RuntimeTrace Append(RuntimeTrace trace, PipelineStage stage, string reason, DateTimeOffset timestamp)
    {
        return trace with
        {
            Entries = Array.AsReadOnly(trace.Entries
                .Concat([new RuntimeTraceEntry(stage, new RuntimeTraceReason(reason), timestamp)])
                .ToArray())
        };
    }
}

public static class RuntimeExecutionFactory
{
    public static RuntimeExecutionContext Create(
        CorrelationId correlationId,
        PaymentSessionId paymentSessionId,
        ExecutionId executionId,
        DateTimeOffset executionTimestamp,
        PipelineMetadata? pipelineMetadata = null,
        ExecutionTraceId? executionTraceId = null)
    {
        var traceId = executionTraceId ?? new ExecutionTraceId($"runtime:{executionId.Value:N}");
        return new RuntimeExecutionContext(
            correlationId,
            paymentSessionId,
            executionId,
            pipelineMetadata,
            new RuntimeTrace(traceId),
            executionTimestamp);
    }
}

public static class RuntimeResultFactory
{
    public static RuntimeResult Succeeded(
        RuntimeExecutionContext context,
        RuntimeTrace executionTrace,
        AdmissionResult admissionResult,
        VerificationResult verificationResult,
        DecisionResult decisionResult,
        TransitionResult transitionResult,
        TransactionResult transactionResult,
        SettlementResult settlementResult,
        AdmittedEvidence admittedEvidence,
        VerifiedEvidence verifiedEvidence,
        PaymentDecision paymentDecision,
        StateTransition stateTransition,
        PaymentTransaction paymentTransaction,
        PaymentSettlement paymentSettlement)
    {
        return new RuntimeResult(
            context,
            RuntimeOverallStatus.Succeeded,
            executionTrace,
            admissionResult,
            verificationResult,
            decisionResult,
            transitionResult,
            transactionResult,
            settlementResult,
            null,
            admittedEvidence,
            verifiedEvidence,
            paymentDecision,
            stateTransition,
            paymentTransaction,
            paymentSettlement);
    }

    public static RuntimeResult Failed(
        RuntimeExecutionContext context,
        RuntimeTrace executionTrace,
        RuntimeFailure failure,
        AdmissionResult? admissionResult = null,
        VerificationResult? verificationResult = null,
        DecisionResult? decisionResult = null,
        TransitionResult? transitionResult = null,
        TransactionResult? transactionResult = null,
        SettlementResult? settlementResult = null,
        AdmittedEvidence? admittedEvidence = null,
        VerifiedEvidence? verifiedEvidence = null,
        PaymentDecision? paymentDecision = null,
        StateTransition? stateTransition = null,
        PaymentTransaction? paymentTransaction = null)
    {
        return new RuntimeResult(
            context,
            RuntimeOverallStatus.Failed,
            executionTrace,
            admissionResult,
            verificationResult,
            decisionResult,
            transitionResult,
            transactionResult,
            settlementResult,
            failure,
            admittedEvidence,
            verifiedEvidence,
            paymentDecision,
            stateTransition,
            paymentTransaction,
            null);
    }
}

internal sealed class EvidenceAdmissionStage : IEvidenceAdmissionStage
{
    private readonly EvidenceAdmissionRuntime runtime = new();

    public AdmissionResult Admit(ProviderEvidence evidence, PaymentAction action)
    {
        return runtime.Admit(evidence, action);
    }
}

internal sealed class EvidenceVerificationStage : IEvidenceVerificationStage
{
    private readonly EvidenceVerificationRuntime runtime = new();

    public VerifiedEvidence Verify(AdmittedEvidence evidence, EvidenceVerificationContext context)
    {
        return runtime.Verify(evidence, context);
    }
}

internal sealed class PaymentDecisionStage : IPaymentDecisionStage
{
    private readonly PaymentDecisionRuntime runtime = new();

    public PaymentDecision Decide(VerifiedEvidence evidence)
    {
        return runtime.Decide(evidence);
    }
}

internal sealed class StateTransitionStage : IStateTransitionStage
{
    private readonly StateTransitionRuntime runtime = new();

    public TransitionResult Transition(
        PaymentDecision decision,
        PaymentSession session,
        IReadOnlyCollection<StateTransition>? priorTransitions = null)
    {
        return runtime.Transition(decision, session, priorTransitions);
    }
}

internal sealed class PaymentTransactionStage : IPaymentTransactionStage
{
    private readonly PaymentTransactionRuntime runtime = new();

    public TransactionResult Produce(
        PaymentDecision decision,
        StateTransition transition,
        TransactionAmount amount,
        Currency currency,
        IReadOnlyCollection<PaymentTransaction>? priorTransactions = null)
    {
        return runtime.Produce(decision, transition, amount, currency, priorTransactions);
    }
}

internal sealed class PaymentSettlementStage : IPaymentSettlementStage
{
    private readonly PaymentSettlementRuntime runtime = new();

    public SettlementResult Produce(
        PaymentTransaction transaction,
        IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
    {
        return runtime.Produce(transaction, priorSettlements);
    }
}
