using System.Reflection;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Domain;
using RuntimeResult = PointOfSale.Core.Payments.Runtime.Results.RuntimeResult;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeOrchestrationCertificationTests
{
    [Fact]
    public void RuntimeOrchestrationTypes_MustExist()
    {
        Assert.True(typeof(ExecutionId).IsValueType);
        Assert.True(typeof(ExecutionTraceId).IsValueType);
        Assert.True(typeof(ExecutionReference).IsValueType);
        Assert.True(typeof(PipelineStage).IsEnum);
        Assert.True(typeof(PipelineMetadata).IsClass);
        Assert.True(typeof(RuntimeExecutionContext).IsClass);
        Assert.True(typeof(RuntimeResult).IsClass);
        Assert.True(typeof(RuntimeFailure).IsClass);

        Assert.Contains(nameof(PipelineStage.ProviderEvidence), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.EvidenceAdmissionRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.EvidenceVerificationRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.PaymentDecisionRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.StateTransitionRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.PaymentTransactionRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.PaymentSettlementRuntime), Enum.GetNames<PipelineStage>());
        Assert.Contains(nameof(PipelineStage.RuntimeResult), Enum.GetNames<PipelineStage>());
    }

    [Fact]
    public void PaymentRuntimeOrchestrator_MustExecutePipelineInCanonicalOrder()
    {
        var calls = new List<string>();
        var orchestrator = CreateOrchestrator(calls);

        var result = orchestrator.Execute(Context(), Evidence(), Action(), Session());

        Assert.Equal(RuntimeOverallStatus.Succeeded, result.OverallStatus);
        Assert.Equal(
            ["admission", "verification", "decision", "transition", "transaction", "settlement"],
            calls);
        Assert.NotNull(result.AdmissionResult);
        Assert.NotNull(result.VerificationResult);
        Assert.NotNull(result.DecisionResult);
        Assert.NotNull(result.TransitionResult);
        Assert.NotNull(result.TransactionResult);
        Assert.NotNull(result.SettlementResult);
        Assert.NotNull(result.PaymentSettlement);
    }

    [Fact]
    public void Failure_MustStopPipelineFailClosedWithoutHiddenStageExecution()
    {
        var calls = new List<string>();
        var orchestrator = CreateOrchestrator(calls, admissionSucceeds: false);

        var result = orchestrator.Execute(Context(), Evidence(), Action(), Session());

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.NotNull(result.Failure);
        Assert.Equal(FailureStage.Admission, result.Failure!.FailureStage);
        Assert.Equal(["admission"], calls);
        Assert.NotNull(result.AdmissionResult);
        Assert.Null(result.VerificationResult);
        Assert.Null(result.DecisionResult);
        Assert.Null(result.TransitionResult);
        Assert.Null(result.TransactionResult);
        Assert.Null(result.SettlementResult);
    }

    [Fact]
    public void InjectedStageLineageMismatch_MustStopBeforeTransition()
    {
        var calls = new List<string>();
        var orchestrator = new PaymentRuntimeOrchestrator(
            new RecordingAdmissionStage(calls, true),
            new RecordingVerificationStage(calls),
            new RecordingDecisionStage(calls, corruptLineage: true),
            new RecordingTransitionStage(calls),
            new RecordingTransactionStage(calls),
            new RecordingSettlementStage(calls));

        var result = orchestrator.Execute(Context(), Evidence(), Action(), Session());

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(FailureStage.Decision, result.Failure!.FailureStage);
        Assert.Equal("DECISION_LINEAGE_INVALID", result.Failure.FailureReason.Value);
        Assert.Equal(["admission", "verification", "decision"], calls);
        Assert.NotNull(result.PaymentDecision);
        Assert.Null(result.StateTransition);
    }

    [Fact]
    public void StageException_MustPreserveEveryCompletedArtifact()
    {
        var calls = new List<string>();
        var orchestrator = new PaymentRuntimeOrchestrator(
            new RecordingAdmissionStage(calls, true),
            new ThrowingVerificationStage(calls),
            new RecordingDecisionStage(calls),
            new RecordingTransitionStage(calls),
            new RecordingTransactionStage(calls),
            new RecordingSettlementStage(calls));

        var result = orchestrator.Execute(Context(), Evidence(), Action(), Session());

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(FailureStage.Verification, result.Failure!.FailureStage);
        Assert.Equal(nameof(InvalidOperationException), result.Failure.FailureReason.Value);
        Assert.Equal(["admission", "verification"], calls);
        Assert.NotNull(result.AdmissionResult);
        Assert.NotNull(result.AdmittedEvidence);
        Assert.Null(result.VerifiedEvidence);
        Assert.Null(result.PaymentDecision);
    }

    [Fact]
    public void Replay_MustFollowIdenticalStageOrder()
    {
        var firstCalls = new List<string>();
        var secondCalls = new List<string>();
        var context = Context();

        var first = CreateOrchestrator(firstCalls).Execute(context, Evidence(), Action(), Session());
        var second = CreateOrchestrator(secondCalls).Execute(context, Evidence(), Action(), Session());

        Assert.Equal(RuntimeOverallStatus.Succeeded, first.OverallStatus);
        Assert.Equal(RuntimeOverallStatus.Succeeded, second.OverallStatus);
        Assert.Equal(firstCalls, secondCalls);
        Assert.Equal(
            first.ExecutionTrace.Entries.Select(entry => entry.Stage),
            second.ExecutionTrace.Entries.Select(entry => entry.Stage));
    }

    [Fact]
    public void Orchestrator_MustDelegateEachRuntimeStageExactlyOnce()
    {
        var calls = new List<string>();

        CreateOrchestrator(calls).Execute(Context(), Evidence(), Action(), Session());

        Assert.Equal(1, calls.Count(call => call == "admission"));
        Assert.Equal(1, calls.Count(call => call == "verification"));
        Assert.Equal(1, calls.Count(call => call == "decision"));
        Assert.Equal(1, calls.Count(call => call == "transition"));
        Assert.Equal(1, calls.Count(call => call == "transaction"));
        Assert.Equal(1, calls.Count(call => call == "settlement"));
    }

    [Fact]
    public void RuntimeExecutionContext_And_RuntimeResult_MustBeImmutableAndSerializableSafe()
    {
        var objectTypes = new[]
        {
            typeof(RuntimeExecutionContext),
            typeof(RuntimeResult),
            typeof(RuntimeFailure),
            typeof(RuntimeTrace),
            typeof(RuntimeTraceEntry),
            typeof(PipelineMetadata)
        };

        foreach (var type in objectTypes)
        {
            var writableProperties = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.SetMethod is not null)
                .ToArray();

            Assert.All(writableProperties, property => Assert.True(IsInitOnly(property), $"{type.Name}.{property.Name} must be init-only."));
        }
    }

    [Fact]
    public void RuntimeExecutionFactory_And_RuntimeResultFactory_MustConstructOnly()
    {
        var context = RuntimeExecutionFactory.Create(
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new ExecutionId(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            DateTimeOffset.Parse("2026-07-10T10:09:00Z"),
            new PipelineMetadata(new Dictionary<string, string> { ["source"] = "certification" }));
        var trace = context.RuntimeTrace with
        {
            Entries = [new RuntimeTraceEntry(PipelineStage.RuntimeResult, new RuntimeTraceReason("FAILED"), context.ExecutionTimestamp)]
        };
        var failure = new RuntimeFailure(FailureStage.Verification, new RuntimeFailureReason("FAILED"), trace);

        var result = RuntimeResultFactory.Failed(context, trace, failure);

        Assert.Equal(context.ExecutionId, result.ExecutionContext.ExecutionId);
        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(failure, result.Failure);
        Assert.Throws<ArgumentException>(() => RuntimeExecutionFactory.Create(
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.Empty),
            new ExecutionId(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            DateTimeOffset.Parse("2026-07-10T10:09:00Z")));
    }

    [Fact]
    public void OrchestratorSource_MustRemainSequencingOnlyAndProviderNeutral()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeOrchestrator.cs");
        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "Vodafone",
            "IPaymentProvider",
            "IPaymentPolicyEngine",
            "PolicyResult",
            "ProviderCapability",
            "ProviderConstraint",
            "DbContext",
            "EntityFramework",
            "MigrationBuilder",
            "SaveChanges",
            "HttpClient",
            "Accounting",
            "Ledger",
            "Treasury",
            "PointOfSale.API",
            "PointOfSale.Infrastructure",
            "PointOfSale.Web",
            "PointOfSale.Storefront",
            "PointOfSale.CommerceHub"
        };

        foreach (var forbidden in forbiddenTerms)
        {
            Assert.DoesNotMatch(
                new Regex($@"\b{Regex.Escape(forbidden)}\b", RegexOptions.CultureInvariant),
                source);
        }

        Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymentPolicyEngine", source, StringComparison.Ordinal);
    }

    private static PaymentRuntimeOrchestrator CreateOrchestrator(List<string> calls, bool admissionSucceeds = true)
    {
        return new PaymentRuntimeOrchestrator(
            new RecordingAdmissionStage(calls, admissionSucceeds),
            new RecordingVerificationStage(calls),
            new RecordingDecisionStage(calls),
            new RecordingTransitionStage(calls),
            new RecordingTransactionStage(calls),
            new RecordingSettlementStage(calls));
    }

    private static RuntimeExecutionContext Context()
    {
        return RuntimeExecutionFactory.Create(
            new CorrelationId("runtime-correlation"),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new ExecutionId(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            DateTimeOffset.Parse("2026-07-10T10:09:00Z"));
    }

    private static PaymentSession Session()
    {
        return PaymentSessionFactory.Create(
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            new PaymentReference("runtime-method"),
            new Money(100m, new Currency("EGP")),
            PaymentStatus.Verified,
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"));
    }

    private static PaymentAction Action()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("action-reference"),
            new ActionCorrelationId("action-correlation"),
            DateTimeOffset.Parse("2026-07-10T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:02:00Z"));
    }

    private static ProviderEvidence Evidence()
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("evidence-reference"),
            new EvidenceSource("runtime-source"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            new EvidenceHash("evidence-hash"),
            new ProviderPayloadReference("payload-reference"),
            new ActionCorrelationId("action-correlation"));
    }

    private static AdmittedEvidence Admitted()
    {
        return AdmittedEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            EvidenceType.ProviderNotification,
            EvidenceStatus.Admitted,
            new EvidenceReference("evidence-reference"),
            new EvidenceSource("runtime-source"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            new EvidenceHash("canonical-hash"),
            new ProviderPayloadReference("payload-reference"),
            new ActionCorrelationId("action-correlation"),
            new AdmissionReference("admission-reference"));
    }

    private static VerifiedEvidence Verified()
    {
        var trace = new VerificationTrace(new VerificationTraceId("verification-trace"));
        var result = new VerificationResult(
            new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new VerificationId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            VerificationStatus.Verified,
            Array.Empty<VerificationFailure>(),
            Array.Empty<VerificationReason>(),
            trace);

        return VerifiedEvidenceFactory.Create(
            result.EvidenceId,
            result.VerificationId,
            new VerificationTimestamp(DateTimeOffset.Parse("2026-07-10T10:04:00Z")),
            new VerificationHash("verification-hash"),
            result,
            trace);
    }

    private static PaymentDecision Decision()
    {
        var trace = new DecisionTrace(new DecisionTraceId("decision-trace"));
        var result = DecisionResultFactory.Create(
            DecisionStatus.Produced,
            new DecisionReason("VERIFICATION_ACCEPTED"),
            DecisionSeverity.Informational,
            trace);

        return PaymentDecisionFactory.Create(
            new DecisionId(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            DecisionType.Approved,
            new DecisionReason("VERIFICATION_ACCEPTED"),
            new DecisionTimestamp(DateTimeOffset.Parse("2026-07-10T10:05:00Z")),
            new DecisionSource("runtime"),
            trace,
            new DecisionMetadata(),
            new VerifiedEvidenceId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            new DecisionCorrelationId("decision-correlation"),
            result);
    }

    private static StateTransition Transition()
    {
        return StateTransitionFactory.Create(
            new TransitionId(Guid.Parse("77777777-7777-7777-7777-777777777777")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new DecisionId(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            PaymentStatus.Verified,
            PaymentStatus.Approved,
            new TransitionReason("APPROVED_TO_PENDING"),
            new TransitionTimestamp(DateTimeOffset.Parse("2026-07-10T10:06:00Z")),
            new TransitionTrace(new TransitionTraceId("transition-trace")));
    }

    private static PaymentTransaction Transaction()
    {
        return PaymentTransactionFactory.Create(
            new TransactionId(Guid.Parse("88888888-8888-8888-8888-888888888888")),
            TransactionType.Authorization,
            TransactionStatus.Created,
            new TransactionAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:07:00Z"),
            new CorrelationId("transaction-correlation"),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new DecisionId(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            new TransitionId(Guid.Parse("77777777-7777-7777-7777-777777777777")));
    }

    private static PaymentSettlement Settlement()
    {
        return PaymentSettlementFactory.Create(
            new SettlementId(Guid.Parse("12121212-1212-1212-1212-121212121212")),
            SettlementType.AuthorizationSettlement,
            SettlementStatus.Pending,
            new SettlementAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:08:00Z"),
            new CorrelationId("settlement-correlation"),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new TransactionId(Guid.Parse("88888888-8888-8888-8888-888888888888")),
            new SettlementReference("settlement-reference"));
    }

    private static bool IsInitOnly(PropertyInfo property)
    {
        var setMethod = property.SetMethod;
        if (setMethod is null)
        {
            return true;
        }

        return setMethod.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    private sealed class RecordingAdmissionStage(List<string> calls, bool succeeds) : IEvidenceAdmissionStage
    {
        public AdmissionResult Admit(ProviderEvidence evidence, PaymentAction action)
        {
            calls.Add("admission");
            return succeeds
                ? AdmissionResult.Accepted(Admitted())
                : AdmissionResult.Rejected(evidence.Id, [new VerificationReason("ADMISSION_REJECTED")]);
        }
    }

    private sealed class RecordingVerificationStage(List<string> calls) : IEvidenceVerificationStage
    {
        public VerifiedEvidence Verify(AdmittedEvidence evidence, EvidenceVerificationContext context)
        {
            calls.Add("verification");
            return Verified();
        }
    }

    private sealed class RecordingDecisionStage(List<string> calls, bool corruptLineage = false) : IPaymentDecisionStage
    {
        public PaymentDecision Decide(VerifiedEvidence evidence)
        {
            calls.Add("decision");
            var decision = Decision();
            return corruptLineage
                ? decision with { VerifiedEvidenceId = new VerifiedEvidenceId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")) }
                : decision;
        }
    }

    private sealed class ThrowingVerificationStage(List<string> calls) : IEvidenceVerificationStage
    {
        public VerifiedEvidence Verify(AdmittedEvidence evidence, EvidenceVerificationContext context)
        {
            calls.Add("verification");
            throw new InvalidOperationException("certification failure");
        }
    }

    private sealed class RecordingTransitionStage(List<string> calls) : IStateTransitionStage
    {
        public TransitionResult Transition(
            PaymentDecision decision,
            PaymentSession session,
            IReadOnlyCollection<StateTransition>? priorTransitions = null)
        {
            calls.Add("transition");
            var transition = PaymentRuntimeOrchestrationCertificationTests.Transition();
            return TransitionResultFactory.Create(
                TransitionStatus.Applied,
                session with { Status = PaymentStatus.Approved },
                transition,
                Array.Empty<TransitionFailure>(),
                new TransitionReason("APPLIED"),
                transition.Trace);
        }
    }

    private sealed class RecordingTransactionStage(List<string> calls) : IPaymentTransactionStage
    {
        public TransactionResult Produce(
            PaymentDecision decision,
            StateTransition transition,
            TransactionAmount amount,
            Currency currency,
            IReadOnlyCollection<PaymentTransaction>? priorTransactions = null)
        {
            calls.Add("transaction");
            var transaction = PaymentRuntimeOrchestrationCertificationTests.Transaction();
            return TransactionResultFactory.Create(
                TransactionStatus.Created,
                transaction,
                Array.Empty<TransactionFailure>(),
                new TransactionReason("CREATED"),
                transaction.Metadata.Values.Count == 0
                    ? new TransactionTrace(new TransactionTraceId("transaction-trace"))
                    : new TransactionTrace(new TransactionTraceId("transaction-trace")));
        }
    }

    private sealed class RecordingSettlementStage(List<string> calls) : IPaymentSettlementStage
    {
        public SettlementResult Produce(
            PaymentTransaction transaction,
            IReadOnlyCollection<PaymentSettlement>? priorSettlements = null)
        {
            calls.Add("settlement");
            var settlement = PaymentRuntimeOrchestrationCertificationTests.Settlement();
            return SettlementResultFactory.Create(
                SettlementStatus.Pending,
                settlement,
                Array.Empty<SettlementFailure>(),
                new SettlementReason("PENDING"),
                new SettlementTrace(new SettlementTraceId("settlement-trace")));
        }
    }
}
