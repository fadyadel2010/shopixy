using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeTransactionEngineCertificationTests
{
    [Fact]
    public void CanonicalTransactionTypesAndValueObjects_MustExist()
    {
        Assert.Contains(nameof(TransactionType.Authorization), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Capture), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Sale), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Refund), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.PartialRefund), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Void), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Adjustment), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Chargeback), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.Reversal), Enum.GetNames<TransactionType>());
        Assert.Contains(nameof(TransactionType.SettlementReservation), Enum.GetNames<TransactionType>());

        Assert.True(typeof(TransactionId).IsValueType);
        Assert.True(typeof(TransactionReference).IsValueType);
        Assert.True(typeof(TransactionAmount).IsValueType);
        Assert.True(typeof(TransactionReason).IsValueType);
        Assert.True(typeof(TransactionTimestamp).IsValueType);
        Assert.True(typeof(TransactionTraceId).IsValueType);
    }

    [Fact]
    public void PaymentTransactionRuntime_MustProduceCanonicalTransactionFromAppliedTransition()
    {
        var decision = SampleDecision(DecisionType.Approved);
        var transition = SampleTransition(decision, PaymentStatus.Approved, PaymentStatus.Completed);

        var result = new PaymentTransactionRuntime().Produce(
            decision,
            transition,
            new TransactionAmount(100m),
            new Currency("egp"));

        Assert.Equal(TransactionStatus.Created, result.Status);
        Assert.Empty(result.Failures);
        Assert.NotNull(result.Transaction);
        Assert.IsAssignableFrom<IPaymentTransaction>(result.Transaction);
        Assert.Equal(TransactionType.Sale, result.Transaction!.TransactionType);
        Assert.Equal(new TransactionAmount(100m), result.Transaction.TransactionAmount);
        Assert.Equal("EGP", result.Transaction.Currency.Code);
        Assert.Equal(transition.SessionId, result.Transaction.PaymentSessionId);
        Assert.Equal(decision.DecisionId, result.Transaction.PaymentDecisionId);
        Assert.Equal(transition.TransitionId, result.Transaction.TransitionId);
    }

    [Fact]
    public void PaymentTransactionRuntime_MustNeverMutateSessionOrDecisionOrTransition()
    {
        var decision = SampleDecision(DecisionType.Approved);
        var transition = SampleTransition(decision, PaymentStatus.Verified, PaymentStatus.Approved);
        var originalDecisionType = decision.DecisionType;
        var originalToStatus = transition.ToStatus;

        var result = new PaymentTransactionRuntime().Produce(
            decision,
            transition,
            new TransactionAmount(100m),
            new Currency("EGP"));

        Assert.Equal(TransactionStatus.Created, result.Status);
        Assert.Equal(originalDecisionType, decision.DecisionType);
        Assert.Equal(originalToStatus, transition.ToStatus);
    }

    [Fact]
    public void PaymentTransactionRuntime_MustValidateOwnershipReplayAndDuplicates()
    {
        var decision = SampleDecision(DecisionType.Approved);
        var transition = SampleTransition(decision, PaymentStatus.Verified, PaymentStatus.Approved);
        var applied = new PaymentTransactionRuntime().Produce(
            decision,
            transition,
            new TransactionAmount(100m),
            new Currency("EGP"));
        var replayed = new PaymentTransactionRuntime().Produce(
            decision,
            transition,
            new TransactionAmount(100m),
            new Currency("EGP"),
            [applied.Transaction!]);
        var wrongOwner = applied.Transaction! with { PaymentSessionId = new PaymentSessionId(Guid.NewGuid()) };
        var ownershipFailure = new PaymentTransactionRuntime().Produce(
            SampleDecision(DecisionType.Approved),
            SampleTransition(SampleDecision(DecisionType.Approved), PaymentStatus.Approved, PaymentStatus.Completed),
            new TransactionAmount(100m),
            new Currency("EGP"),
            [wrongOwner]);
        var duplicatedPrior = applied.Transaction! with { TransactionId = new TransactionId(Guid.NewGuid()) };
        var duplicateFailure = new PaymentTransactionRuntime().Produce(
            SampleDecision(DecisionType.Approved),
            SampleTransition(SampleDecision(DecisionType.Approved), PaymentStatus.Approved, PaymentStatus.Completed),
            new TransactionAmount(100m),
            new Currency("EGP"),
            [applied.Transaction!, duplicatedPrior]);

        Assert.Equal(TransactionStatus.Replayed, replayed.Status);
        Assert.Contains(TransactionFailure.ReplayDetected, replayed.Failures);
        Assert.Null(replayed.Transaction);

        Assert.Equal(TransactionStatus.Rejected, ownershipFailure.Status);
        Assert.Contains(TransactionFailure.OwnershipInvalid, ownershipFailure.Failures);

        Assert.Equal(TransactionStatus.Duplicate, duplicateFailure.Status);
        Assert.Contains(TransactionFailure.DuplicateTransaction, duplicateFailure.Failures);
    }

    [Fact]
    public void Factories_MustConstructOnlyAndValidateInvariants()
    {
        var transaction = PaymentTransactionFactory.Create(
            new TransactionId(Guid.NewGuid()),
            TransactionType.Authorization,
            TransactionStatus.Created,
            new TransactionAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:04:00Z"),
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.NewGuid()),
            new DecisionId(Guid.NewGuid()),
            new TransitionId(Guid.NewGuid()),
            new TransactionMetadata(new Dictionary<string, string> { ["source"] = "certification" }));
        var trace = new TransactionTrace(new TransactionTraceId("transaction-trace"));
        var result = TransactionResultFactory.Create(
            TransactionStatus.Created,
            transaction,
            Array.Empty<TransactionFailure>(),
            new TransactionReason("AUTHORIZED"),
            trace);

        Assert.Equal(TransactionType.Authorization, transaction.TransactionType);
        Assert.Equal(TransactionStatus.Created, result.Status);
        Assert.Throws<ArgumentException>(() => PaymentTransactionFactory.Create(
            new TransactionId(Guid.Empty),
            TransactionType.Authorization,
            TransactionStatus.Created,
            new TransactionAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:04:00Z"),
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.NewGuid()),
            new DecisionId(Guid.NewGuid()),
            new TransitionId(Guid.NewGuid())));
    }

    [Fact]
    public void PaymentTransaction_MustReferenceSessionAndDecisionByIdentifiersOnly()
    {
        var propertyTypes = typeof(PaymentTransaction)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Contains(typeof(PaymentSessionId), propertyTypes);
        Assert.Contains(typeof(DecisionId), propertyTypes);
        Assert.Contains(typeof(TransitionId), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentSession), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentDecision), propertyTypes);
        Assert.DoesNotContain(typeof(StateTransition), propertyTypes);
        Assert.DoesNotContain(typeof(VerifiedEvidence), propertyTypes);
    }

    [Fact]
    public void TransactionObjects_MustUseInitOnlyPropertiesAndRoundTripThroughJson()
    {
        var decision = SampleDecision(DecisionType.Approved);
        var transition = SampleTransition(decision, PaymentStatus.Approved, PaymentStatus.Completed);
        var result = new PaymentTransactionRuntime().Produce(
            decision,
            transition,
            new TransactionAmount(100m),
            new Currency("EGP"));
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<TransactionResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(result.Status, deserialized.Status);
        Assert.Equal(result.Transaction!.TransactionType, deserialized.Transaction!.TransactionType);
        Assert.Equal(result.Transaction.PaymentSessionId, deserialized.Transaction.PaymentSessionId);

        var objectTypes = new[]
        {
            typeof(PaymentTransaction),
            typeof(TransactionResult),
            typeof(TransactionTrace),
            typeof(TransactionMetadata)
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
    public void PaymentTransactionRuntimeSource_MustRemainProviderNeutralAndFinancialBoundaryOnly()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentTransactionRuntime.cs");
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
            "PaymentSessionFactory",
            "PaymentDecisionRuntime",
            "EvidenceVerificationRuntime",
            "SettlementResult",
            "Accounting",
            "Ledger",
            "ProviderCapability",
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

        Assert.DoesNotContain("with\r\n        {\r\n            Status =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSession ", source, StringComparison.Ordinal);
        Assert.Contains("PaymentSessionId", source, StringComparison.Ordinal);
        Assert.Contains("PaymentDecisionId", source, StringComparison.Ordinal);
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

    private static StateTransition SampleTransition(PaymentDecision decision, PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        var transitionId = new TransitionId(Guid.NewGuid());
        var trace = new TransitionTrace(new TransitionTraceId($"transition:{transitionId.Value:N}"), [new TransitionReason("CERTIFICATION_TRANSITION")]);

        return StateTransitionFactory.Create(
            transitionId,
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            decision.DecisionId,
            fromStatus,
            toStatus,
            new TransitionReason("CERTIFICATION_TRANSITION"),
            new TransitionTimestamp(DateTimeOffset.Parse("2026-07-10T10:03:00Z")),
            trace);
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
