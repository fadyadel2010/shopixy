using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeDecisionEngineCertificationTests
{
    [Fact]
    public void CanonicalDecisionTypesAndValueObjects_MustExist()
    {
        Assert.Contains(nameof(DecisionType.Approved), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Rejected), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.ManualReview), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Retry), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Cancelled), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Expired), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Recovery), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Quarantine), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.FraudReview), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.ComplianceReview), Enum.GetNames<DecisionType>());
        Assert.Contains(nameof(DecisionType.Unknown), Enum.GetNames<DecisionType>());

        Assert.True(typeof(DecisionId).IsValueType);
        Assert.True(typeof(DecisionReason).IsValueType);
        Assert.True(typeof(DecisionTraceId).IsValueType);
        Assert.True(typeof(DecisionTimestamp).IsValueType);
        Assert.True(typeof(DecisionCorrelationId).IsValueType);
        Assert.True(typeof(DecisionReference).IsValueType);
        Assert.True(typeof(VerifiedEvidenceId).IsValueType);
    }

    [Fact]
    public void PaymentDecisionRuntime_MustProduceDecisionFromVerifiedEvidence()
    {
        var verified = SampleVerifiedEvidence(VerificationStatus.Verified);

        var decision = new PaymentDecisionRuntime().Decide(verified);

        Assert.IsAssignableFrom<IPaymentDecision>(decision);
        Assert.NotEqual(Guid.Empty, decision.DecisionId.Value);
        Assert.Equal(DecisionType.Approved, decision.DecisionType);
        Assert.Equal("VERIFICATION_ACCEPTED", decision.DecisionReason.Value);
        Assert.Equal(verified.VerificationId.Value, decision.VerifiedEvidenceId.Value);
        Assert.Equal(verified.EvidenceId.Value.ToString("N"), decision.CorrelationId.Value);
        Assert.Equal(DecisionStatus.Produced, decision.Result.Status);
    }

    [Fact]
    public void PaymentDecisionRuntime_MustMapCanonicalVerificationFailuresOnly()
    {
        var rejected = new PaymentDecisionRuntime().Decide(SampleVerifiedEvidence(
            VerificationStatus.Failed,
            [VerificationFailure.HashInvalid],
            [new VerificationReason("HASH_INVALID")]));

        var quarantined = new PaymentDecisionRuntime().Decide(SampleVerifiedEvidence(
            VerificationStatus.Failed,
            [VerificationFailure.DuplicateEvidence],
            [new VerificationReason("DUPLICATE_EVIDENCE")]));

        var recovery = new PaymentDecisionRuntime().Decide(SampleVerifiedEvidence(
            VerificationStatus.Failed,
            [VerificationFailure.OrderingInvalid],
            [new VerificationReason("ORDERING_INVALID")]));

        Assert.Equal(DecisionType.Rejected, rejected.DecisionType);
        Assert.Equal(DecisionType.Quarantine, quarantined.DecisionType);
        Assert.Equal(DecisionType.Recovery, recovery.DecisionType);
    }

    [Fact]
    public void PaymentDecision_MustReferenceVerifiedEvidenceOnly()
    {
        var decision = new PaymentDecisionRuntime().Decide(SampleVerifiedEvidence(VerificationStatus.Verified));

        Assert.NotEqual(Guid.Empty, decision.VerifiedEvidenceId.Value);

        var propertyTypes = typeof(PaymentDecision)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(PaymentSession), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentSessionId), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentAttempt), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentAttemptId), propertyTypes);
    }

    [Fact]
    public void Factories_MustConstructOnlyAndValidateInvariants()
    {
        var verified = SampleVerifiedEvidence(VerificationStatus.Verified);
        var trace = new DecisionTrace(new DecisionTraceId("decision-trace"), [new DecisionReason("VERIFICATION_ACCEPTED")]);
        var metadata = new DecisionMetadata(new Dictionary<string, string> { ["source"] = "verification" });
        var result = DecisionResultFactory.Create(
            DecisionStatus.Produced,
            new DecisionReason("VERIFICATION_ACCEPTED"),
            DecisionSeverity.Informational,
            trace,
            metadata);

        var decision = PaymentDecisionFactory.Create(
            new DecisionId(Guid.NewGuid()),
            DecisionType.Approved,
            new DecisionReason("VERIFICATION_ACCEPTED"),
            new DecisionTimestamp(DateTimeOffset.Parse("2026-07-10T10:02:00Z")),
            new DecisionSource("runtime-verification"),
            trace,
            metadata,
            new VerifiedEvidenceId(verified.VerificationId.Value),
            new DecisionCorrelationId("correlation"),
            result);

        Assert.Equal(DecisionType.Approved, decision.DecisionType);
        Assert.Throws<ArgumentException>(() => PaymentDecisionFactory.Create(
            new DecisionId(Guid.Empty),
            DecisionType.Approved,
            new DecisionReason("VERIFICATION_ACCEPTED"),
            new DecisionTimestamp(DateTimeOffset.Parse("2026-07-10T10:02:00Z")),
            new DecisionSource("runtime-verification"),
            trace,
            metadata,
            new VerifiedEvidenceId(verified.VerificationId.Value),
            new DecisionCorrelationId("correlation"),
            result));
    }

    [Fact]
    public void DecisionObjects_MustUseInitOnlyProperties()
    {
        var objectTypes = new[]
        {
            typeof(PaymentDecision),
            typeof(DecisionResult),
            typeof(DecisionTrace),
            typeof(DecisionMetadata)
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
    public void PaymentDecision_MustRoundTripThroughJsonSerialization()
    {
        var decision = new PaymentDecisionRuntime().Decide(SampleVerifiedEvidence(VerificationStatus.Verified));

        var json = JsonSerializer.Serialize(decision);
        var deserialized = JsonSerializer.Deserialize<PaymentDecision>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(decision.DecisionId, deserialized.DecisionId);
        Assert.Equal(decision.DecisionType, deserialized.DecisionType);
        Assert.Equal(decision.VerifiedEvidenceId, deserialized.VerifiedEvidenceId);
        Assert.Equal(decision.DecisionTrace.Id, deserialized.DecisionTrace.Id);
    }

    [Fact]
    public void DecisionRuntimeSource_MustRemainProviderNeutralAndIsolated()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentDecisionRuntime.cs");
        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "ManualWallet",
            "Vodafone",
            "PaymentSession",
            "PaymentAttempt",
            "PaymentSessionFactory",
            "PaymentAttemptFactory",
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
                source);
        }
    }

    private static VerifiedEvidence SampleVerifiedEvidence(
        VerificationStatus status,
        IReadOnlyCollection<VerificationFailure>? failures = null,
        IReadOnlyCollection<VerificationReason>? reasons = null)
    {
        var evidenceId = new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var verificationId = new VerificationId(Guid.NewGuid());
        var trace = new VerificationTrace(new VerificationTraceId($"verification:{verificationId.Value:N}"));
        var result = new VerificationResult(
            evidenceId,
            verificationId,
            status,
            failures ?? Array.Empty<VerificationFailure>(),
            reasons ?? Array.Empty<VerificationReason>(),
            trace);

        return VerifiedEvidenceFactory.Create(
            evidenceId,
            verificationId,
            new VerificationTimestamp(DateTimeOffset.Parse("2026-07-10T10:01:00Z")),
            new VerificationHash("sha256:verified"),
            result,
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
