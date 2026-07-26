using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeStateTransitionsCertificationTests
{
    [Fact]
    public void CanonicalTransitionModelAndStates_MustExist()
    {
        Assert.Contains(nameof(PaymentStatus.Created), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Pending), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.AwaitingCustomer), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.AwaitingProvider), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Verified), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Approved), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Rejected), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Cancelled), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Expired), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Completed), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Quarantined), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.ManualReview), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Recovery), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.SettlementPending), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Settled), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.RefundPending), Enum.GetNames<PaymentStatus>());
        Assert.Contains(nameof(PaymentStatus.Refunded), Enum.GetNames<PaymentStatus>());

        Assert.True(typeof(TransitionId).IsValueType);
        Assert.True(typeof(TransitionReference).IsValueType);
        Assert.True(typeof(TransitionTimestamp).IsValueType);
        Assert.True(typeof(TransitionTraceId).IsValueType);
        Assert.True(typeof(TransitionReason).IsValueType);
    }

    [Fact]
    public void StateTransitionRuntime_MustApplyLegalCanonicalTransitions()
    {
        var runtime = new StateTransitionRuntime();
        var first = runtime.Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.Created));
        var second = runtime.Transition(SampleDecision(DecisionType.Approved), first.Session, [first.Transition!]);
        var third = runtime.Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.AwaitingCustomer));
        var fourth = runtime.Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.Verified));
        var fifth = runtime.Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.Approved));
        var sixth = runtime.Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.Completed));

        Assert.Equal(TransitionStatus.Applied, first.Status);
        Assert.Equal(PaymentStatus.Pending, first.Session.Status);
        Assert.Equal(PaymentStatus.AwaitingCustomer, second.Session.Status);
        Assert.Equal(PaymentStatus.Verified, third.Session.Status);
        Assert.Equal(PaymentStatus.Approved, fourth.Session.Status);
        Assert.Equal(PaymentStatus.Completed, fifth.Session.Status);
        Assert.Equal(PaymentStatus.SettlementPending, sixth.Session.Status);
    }

    [Fact]
    public void StateTransitionRuntime_MustRejectIllegalTransitionsWithoutChangingSession()
    {
        var session = SampleSession(PaymentStatus.Settled);
        var result = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Approved), session);

        Assert.Equal(TransitionStatus.Rejected, result.Status);
        Assert.Null(result.Transition);
        Assert.Equal(PaymentStatus.Settled, result.Session.Status);
        Assert.Same(session, result.Session);
        Assert.Contains(TransitionFailure.TransitionIllegal, result.Failures);
    }

    [Fact]
    public void StateTransitionRuntime_MustValidateOwnershipUniquenessAndReplay()
    {
        var session = SampleSession(PaymentStatus.Created);
        var decision = SampleDecision(DecisionType.Approved);
        var applied = new StateTransitionRuntime().Transition(decision, session);

        var replayed = new StateTransitionRuntime().Transition(decision, applied.Session, [applied.Transition!]);
        var wrongOwner = applied.Transition! with { SessionId = new PaymentSessionId(Guid.NewGuid()) };
        var ownershipFailure = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Approved), applied.Session, [wrongOwner]);

        Assert.Equal(TransitionStatus.Replayed, replayed.Status);
        Assert.Contains(TransitionFailure.ReplayDetected, replayed.Failures);
        Assert.Equal(PaymentStatus.Pending, replayed.Session.Status);

        Assert.Equal(TransitionStatus.Rejected, ownershipFailure.Status);
        Assert.Contains(TransitionFailure.OwnershipInvalid, ownershipFailure.Failures);
    }

    [Fact]
    public void StateTransitionRuntime_MustBeDeterministicForSameStateAndDecisionType()
    {
        var left = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Quarantine), SampleSession(PaymentStatus.Pending));
        var right = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Quarantine), SampleSession(PaymentStatus.Pending));

        Assert.Equal(TransitionStatus.Applied, left.Status);
        Assert.Equal(TransitionStatus.Applied, right.Status);
        Assert.Equal(left.Session.Status, right.Session.Status);
        Assert.Equal(PaymentStatus.Quarantined, left.Session.Status);
    }

    [Fact]
    public void StateTransitionRuntime_MustProduceNewImmutablePaymentSessionOnlyThroughRuntime()
    {
        var session = SampleSession(PaymentStatus.Created);
        var result = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Approved), session);

        Assert.NotSame(session, result.Session);
        Assert.Equal(PaymentStatus.Created, session.Status);
        Assert.Equal(PaymentStatus.Pending, result.Session.Status);
        Assert.Equal(session.Id, result.Session.Id);
        Assert.Equal(session.Attempts, result.Session.Attempts);
        Assert.Equal(session.ProviderSessions, result.Session.ProviderSessions);
    }

    [Fact]
    public void Factories_MustConstructOnlyAndValidateInvariants()
    {
        var trace = new TransitionTrace(new TransitionTraceId("transition-trace"));
        var transition = StateTransitionFactory.Create(
            new TransitionId(Guid.NewGuid()),
            new PaymentSessionId(Guid.NewGuid()),
            new DecisionId(Guid.NewGuid()),
            PaymentStatus.Created,
            PaymentStatus.Pending,
            new TransitionReason("APPROVED_TO_PENDING"),
            new TransitionTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            trace);

        var result = TransitionResultFactory.Create(
            TransitionStatus.Applied,
            SampleSession(PaymentStatus.Pending),
            transition,
            Array.Empty<TransitionFailure>(),
            new TransitionReason("APPROVED_TO_PENDING"),
            trace);

        Assert.Equal(PaymentStatus.Pending, transition.ToStatus);
        Assert.Equal(TransitionStatus.Applied, result.Status);
        Assert.Throws<ArgumentException>(() => StateTransitionFactory.Create(
            new TransitionId(Guid.Empty),
            new PaymentSessionId(Guid.NewGuid()),
            new DecisionId(Guid.NewGuid()),
            PaymentStatus.Created,
            PaymentStatus.Pending,
            new TransitionReason("APPROVED_TO_PENDING"),
            new TransitionTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            trace));
    }

    [Fact]
    public void TransitionObjects_MustUseInitOnlyPropertiesAndRoundTripThroughJson()
    {
        var result = new StateTransitionRuntime().Transition(SampleDecision(DecisionType.Approved), SampleSession(PaymentStatus.Created));
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<TransitionResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(result.Status, deserialized.Status);
        Assert.Equal(result.Session.Status, deserialized.Session.Status);
        Assert.Equal(result.Transition!.ToStatus, deserialized.Transition!.ToStatus);

        var objectTypes = new[]
        {
            typeof(StateTransition),
            typeof(TransitionResult),
            typeof(TransitionTrace),
            typeof(TransitionMetadata)
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
    public void StateTransitionRuntimeSource_MustRemainProviderNeutralAndOnlyStateAuthority()
    {
        var transitionSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentStateTransitionRuntime.cs");
        var decisionSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentDecisionRuntime.cs");
        var verificationSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentEvidenceVerificationRuntime.cs");
        var objectSource = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs");

        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "ManualWallet",
            "Vodafone",
            "IPaymentPolicyEngine",
            "PolicyResult",
            "DbContext",
            "EntityFramework",
            "MigrationBuilder",
            "SaveChanges",
            "HttpClient",
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
                transitionSource);
        }

        Assert.DoesNotContain("PaymentSessionFactory.Create", transitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("with\r\n        {\r\n            Status =", decisionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("with\r\n        {\r\n            Status =", verificationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("with\r\n        {\r\n            Status =", objectSource, StringComparison.Ordinal);
        Assert.Contains("StateTransitionRuntime", transitionSource, StringComparison.Ordinal);
        Assert.Contains("Status = targetStatus", transitionSource, StringComparison.Ordinal);
    }

    private static PaymentSession SampleSession(PaymentStatus status)
    {
        var created = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var paymentId = new PaymentId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        return PaymentSessionFactory.Create(
            sessionId,
            paymentId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            new PaymentReference("card"),
            new Money(100m, new Currency("EGP")),
            status,
            created,
            created);
    }

    private static PaymentDecision SampleDecision(DecisionType type)
    {
        var decisionId = new DecisionId(Guid.NewGuid());
        var trace = new DecisionTrace(new DecisionTraceId($"decision:{decisionId.Value:N}"), [new DecisionReason("CERTIFICATION_DECISION")]);
        var metadata = new DecisionMetadata(new Dictionary<string, string> { ["source"] = "certification" });
        var result = DecisionResultFactory.Create(
            DecisionStatus.Produced,
            new DecisionReason("CERTIFICATION_DECISION"),
            DecisionSeverity.Informational,
            trace,
            metadata);

        return PaymentDecisionFactory.Create(
            decisionId,
            type,
            new DecisionReason("CERTIFICATION_DECISION"),
            new DecisionTimestamp(DateTimeOffset.Parse("2026-07-10T10:02:00Z")),
            new DecisionSource("runtime-verification"),
            trace,
            metadata,
            new VerifiedEvidenceId(Guid.NewGuid()),
            new DecisionCorrelationId("certification-correlation"),
            result);
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
}
