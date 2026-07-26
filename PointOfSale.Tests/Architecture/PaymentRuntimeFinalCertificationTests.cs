using System.Text.Json;
using PointOfSale.Core.Payments.Runtime.Domain;
using RuntimeResult = PointOfSale.Core.Payments.Runtime.Results.RuntimeResult;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeFinalCertificationTests
{
    public static TheoryData<string, PaymentActionType, EvidenceType, PaymentStatus, PaymentStatus, TransactionType, SettlementType> GoldenScenarios => new()
    {
        { "Stripe-like sale", PaymentActionType.SdkConfirmation, EvidenceType.Webhook, PaymentStatus.Approved, PaymentStatus.Completed, TransactionType.Sale, SettlementType.SaleSettlement },
        { "Stripe-like refund", PaymentActionType.StatusRefresh, EvidenceType.RefundEvidence, PaymentStatus.RefundPending, PaymentStatus.Refunded, TransactionType.Refund, SettlementType.RefundSettlement },
        { "Paymob callback", PaymentActionType.Redirect, EvidenceType.Callback, PaymentStatus.Verified, PaymentStatus.Approved, TransactionType.Authorization, SettlementType.AuthorizationSettlement },
        { "Manual receipt", PaymentActionType.ReceiptUpload, EvidenceType.Receipt, PaymentStatus.Approved, PaymentStatus.Completed, TransactionType.Sale, SettlementType.SaleSettlement },
        { "COD delivery", PaymentActionType.ReceiveShipment, EvidenceType.DriverConfirmation, PaymentStatus.Collected, PaymentStatus.SettlementPending, TransactionType.SettlementReservation, SettlementType.ReservationSettlement }
    };

    [Theory]
    [MemberData(nameof(GoldenScenarios))]
    public void GoldenScenario_MustExecuteThroughCanonicalRealEnginePipeline(
        string scenario,
        PaymentActionType actionType,
        EvidenceType evidenceType,
        PaymentStatus initialStatus,
        PaymentStatus expectedStatus,
        TransactionType expectedTransaction,
        SettlementType expectedSettlement)
    {
        var fixture = CreateFixture(actionType, evidenceType, initialStatus, scenario);

        var result = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);

        Assert.Equal(RuntimeOverallStatus.Succeeded, result.OverallStatus);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedTransaction, result.PaymentTransaction!.TransactionType);
        Assert.Equal(expectedSettlement, result.PaymentSettlement!.SettlementType);
        Assert.Equal(
            [
                PipelineStage.ProviderEvidence,
                PipelineStage.EvidenceAdmissionRuntime,
                PipelineStage.EvidenceVerificationRuntime,
                PipelineStage.PaymentDecisionRuntime,
                PipelineStage.StateTransitionRuntime,
                PipelineStage.PaymentTransactionRuntime,
                PipelineStage.PaymentSettlementRuntime,
                PipelineStage.RuntimeResult
            ],
            result.ExecutionTrace.Entries.Select(entry => entry.Stage));
    }

    [Fact]
    public void SameCanonicalInput_MustProduceSameCompleteRuntimeResult()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "deterministic-replay");
        var runtime = new PaymentRuntimeOrchestrator();

        var first = runtime.Execute(fixture.Context, fixture.Evidence, fixture.Action, fixture.Session);
        var replay = runtime.Execute(fixture.Context, fixture.Evidence, fixture.Action, fixture.Session);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(first.VerifiedEvidence!.VerificationId, replay.VerifiedEvidence!.VerificationId);
        Assert.Equal(first.PaymentDecision!.DecisionId, replay.PaymentDecision!.DecisionId);
        Assert.Equal(first.StateTransition!.TransitionId, replay.StateTransition!.TransitionId);
        Assert.Equal(first.PaymentTransaction!.TransactionId, replay.PaymentTransaction!.TransactionId);
        Assert.Equal(first.PaymentSettlement!.SettlementId, replay.PaymentSettlement!.SettlementId);
    }

    [Fact]
    public void DuplicateAndOutOfOrderEvidence_MustFailAtVerificationWithoutFinancialArtifacts()
    {
        var fixture = CreateFixture(
            PaymentActionType.StatusRefresh,
            EvidenceType.ProviderNotification,
            PaymentStatus.Approved,
            "duplicate-ordering");
        var admission = new EvidenceAdmissionRuntime().Admit(fixture.Evidence, fixture.Action).Evidence!;
        var newer = admission with
        {
            EvidenceId = new EvidenceId(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
            EvidenceTimestamp = new EvidenceTimestamp(admission.EvidenceTimestamp.Value.AddMinutes(1))
        };

        var result = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session,
            [admission, newer]);

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(FailureStage.Verification, result.Failure!.FailureStage);
        Assert.Contains(VerificationFailure.DuplicateEvidence, result.VerificationResult!.Failures);
        Assert.Contains(VerificationFailure.OrderingInvalid, result.VerificationResult.Failures);
        Assert.Null(result.PaymentDecision);
        Assert.Null(result.StateTransition);
        Assert.Null(result.PaymentTransaction);
        Assert.Null(result.PaymentSettlement);
    }

    [Fact]
    public void RecoveryEvidence_MustRemainDecisionAndTransitionOwned()
    {
        var fixture = CreateFixture(
            PaymentActionType.StatusRefresh,
            EvidenceType.StatusInquiry,
            PaymentStatus.AwaitingProvider,
            "recovery");
        var admitted = new EvidenceAdmissionRuntime().Admit(fixture.Evidence, fixture.Action).Evidence!;
        var newer = admitted with
        {
            EvidenceId = new EvidenceId(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")),
            EvidenceReference = new EvidenceReference("newer-evidence"),
            EvidenceHash = new EvidenceHash("different-hash"),
            EvidenceTimestamp = new EvidenceTimestamp(admitted.EvidenceTimestamp.Value.AddMinutes(1))
        };
        var verification = new EvidenceVerificationRuntime().Verify(
            admitted,
            new EvidenceVerificationContext(fixture.Action, [newer]));

        var decision = new PaymentDecisionRuntime().Decide(verification);
        var transition = new StateTransitionRuntime().Transition(decision, fixture.Session);

        Assert.Equal(DecisionType.Recovery, decision.DecisionType);
        Assert.Equal(TransitionStatus.Applied, transition.Status);
        Assert.Equal(PaymentStatus.Recovery, transition.Session.Status);
    }

    [Fact]
    public void RetryAndNonFinancialProgression_MustNotInventTransactionOrSettlement()
    {
        var fixture = CreateFixture(
            PaymentActionType.ActionRetry,
            EvidenceType.StatusInquiry,
            PaymentStatus.ActionRequired,
            "retry");

        var result = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(FailureStage.Transaction, result.Failure!.FailureStage);
        Assert.Equal(PaymentStatus.AwaitingCustomer, result.Status);
        Assert.Equal(PaymentStatus.AwaitingCustomer, result.TransitionResult!.Session.Status);
        Assert.Contains(TransactionFailure.TransitionNotApplied, result.TransactionResult!.Failures);
        Assert.Null(result.PaymentTransaction);
        Assert.Null(result.PaymentSettlement);
    }

    [Fact]
    public void StaleSessionWithLaterTransitionHistory_MustFailClosed()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "stale-session");
        var completed = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);
        var nextDecision = completed.PaymentDecision! with
        {
            DecisionId = new DecisionId(Guid.Parse("abababab-abab-abab-abab-abababababab"))
        };

        var result = new StateTransitionRuntime().Transition(
            nextDecision,
            fixture.Session,
            [completed.StateTransition!]);

        Assert.Equal(TransitionStatus.Rejected, result.Status);
        Assert.Contains(TransitionFailure.CurrentStateInvalid, result.Failures);
        Assert.Null(result.Transition);
    }

    [Fact]
    public void FabricatedDecisionTransitionMeaning_MustNotCreateFinancialArtifact()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "fabricated-transition");
        var completed = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);
        var fabricated = StateTransitionFactory.Create(
            new TransitionId(Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd")),
            fixture.Session.Id,
            completed.PaymentDecision!.DecisionId,
            PaymentStatus.Approved,
            PaymentStatus.Refunded,
            new TransitionReason("FABRICATED"),
            new TransitionTimestamp(completed.PaymentDecision.DecisionTime.Value),
            new TransitionTrace(new TransitionTraceId("fabricated")));

        var result = new PaymentTransactionRuntime().Produce(
            completed.PaymentDecision,
            fabricated,
            new TransactionAmount(100m),
            new Currency("EGP"));

        Assert.Equal(TransactionStatus.Rejected, result.Status);
        Assert.Contains(TransactionFailure.TransitionNotApplied, result.Failures);
        Assert.Null(result.Transaction);
    }

    [Fact]
    public void NonCreatedTransaction_MustNotCreateSettlement()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "rejected-transaction");
        var completed = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);
        var rejected = completed.PaymentTransaction! with { TransactionStatus = TransactionStatus.Rejected };

        var result = new PaymentSettlementRuntime().Produce(rejected);

        Assert.Equal(SettlementStatus.Rejected, result.Status);
        Assert.Contains(SettlementFailure.TransactionInvalid, result.Failures);
        Assert.Null(result.Settlement);
    }

    [Fact]
    public void DecisionOlderThanSessionState_MustNotMoveSessionClockBackward()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "timestamp-regression");
        var completed = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);
        var newerSession = fixture.Session with
        {
            LastUpdatedUtc = completed.PaymentDecision!.DecisionTime.Value.AddMinutes(1)
        };

        var result = new StateTransitionRuntime().Transition(completed.PaymentDecision, newerSession);

        Assert.Equal(TransitionStatus.Rejected, result.Status);
        Assert.Contains(TransitionFailure.DecisionInvalid, result.Failures);
        Assert.Equal(newerSession.LastUpdatedUtc, result.Session.LastUpdatedUtc);
    }

    [Fact]
    public void EquivalentMetadataInsertionOrder_MustSerializeDeterministically()
    {
        var first = new PaymentMetadata(new Dictionary<string, string>
        {
            ["zeta"] = "last",
            ["alpha"] = "first"
        });
        var second = new PaymentMetadata(new Dictionary<string, string>
        {
            ["alpha"] = "first",
            ["zeta"] = "last"
        });

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void SessionLineageMismatch_MustFailBeforeAdmission()
    {
        var fixture = CreateFixture(
            PaymentActionType.StatusRefresh,
            EvidenceType.ProviderNotification,
            PaymentStatus.Approved,
            "lineage");
        var wrongSession = fixture.Session with
        {
            Id = new PaymentSessionId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))
        };

        var result = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            wrongSession);

        Assert.Equal(RuntimeOverallStatus.Failed, result.OverallStatus);
        Assert.Equal(FailureStage.ProviderEvidence, result.Failure!.FailureStage);
        Assert.Equal("SESSION_LINEAGE_INVALID", result.Failure.FailureReason.Value);
        Assert.Null(result.AdmissionResult);
    }

    [Fact]
    public void RuntimeObjects_MustDefensivelyFreezeInputCollections()
    {
        var values = new Dictionary<string, string> { ["source"] = "original" };
        var attempts = new List<PaymentAttempt>();
        var metadata = new PaymentMetadata(values);
        var fixture = CreateFixture(
            PaymentActionType.StatusRefresh,
            EvidenceType.ProviderNotification,
            PaymentStatus.Approved,
            "immutability",
            metadata,
            attempts);

        values["source"] = "mutated";
        attempts.Add(new PaymentAttempt());

        Assert.Equal("original", fixture.Session.Metadata.Values["source"]);
        Assert.Empty(fixture.Session.Attempts);
        Assert.Throws<NotSupportedException>(() => ((ICollection<PaymentAttempt>)fixture.Session.Attempts).Add(new PaymentAttempt()));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)fixture.Session.Metadata.Values).Add("new", "value"));
    }

    [Fact]
    public void CompleteRuntimeResult_MustRoundTripThroughJson()
    {
        var fixture = CreateFixture(
            PaymentActionType.SdkConfirmation,
            EvidenceType.Webhook,
            PaymentStatus.Approved,
            "serialization");
        var result = new PaymentRuntimeOrchestrator().Execute(
            fixture.Context,
            fixture.Evidence,
            fixture.Action,
            fixture.Session);

        var json = JsonSerializer.Serialize(result);
        var roundTrip = JsonSerializer.Deserialize<RuntimeResult>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.OverallStatus, roundTrip.OverallStatus);
        Assert.Equal(result.SessionId, roundTrip.SessionId);
        Assert.Equal(result.PaymentSettlement!.SettlementId, roundTrip.PaymentSettlement!.SettlementId);
    }

    private static Fixture CreateFixture(
        PaymentActionType actionType,
        EvidenceType evidenceType,
        PaymentStatus initialStatus,
        string scenario,
        PaymentMetadata? metadata = null,
        IReadOnlyCollection<PaymentAttempt>? attempts = null)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var actionId = new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var attemptId = new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var executionTime = DateTimeOffset.Parse("2026-07-10T10:09:00Z");
        var actionCorrelation = new ActionCorrelationId($"canonical-{scenario}");
        var session = PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            new PaymentReference("canonical-method"),
            new Money(100m, new Currency("EGP")),
            initialStatus,
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            metadata,
            attempts: attempts);
        var action = PaymentActionFactory.Create(
            actionId,
            sessionId,
            attemptId,
            actionType,
            PaymentActionStatus.Completed,
            new ActionReference($"action-{scenario}"),
            actionCorrelation,
            DateTimeOffset.Parse("2026-07-10T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:02:00Z"));
        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            actionId,
            evidenceType,
            EvidenceStatus.Recorded,
            new EvidenceReference($"evidence-{scenario}"),
            new EvidenceSource("canonical-source"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            new EvidenceHash("candidate-hash"),
            new ProviderPayloadReference($"payload-{scenario}"),
            actionCorrelation);
        var context = RuntimeExecutionFactory.Create(
            new CorrelationId($"runtime-{scenario}"),
            sessionId,
            new ExecutionId(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            executionTime);

        return new Fixture(context, session, action, evidence);
    }

    private sealed record Fixture(
        RuntimeExecutionContext Context,
        PaymentSession Session,
        PaymentAction Action,
        ProviderEvidence Evidence);
}
