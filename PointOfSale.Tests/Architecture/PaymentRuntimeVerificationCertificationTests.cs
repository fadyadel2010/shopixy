using System.Reflection;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeVerificationCertificationTests
{
    [Fact]
    public void AdmissionRuntime_MustAdmitProviderEvidenceIntoImmutableAdmittedEvidence()
    {
        var action = SampleAction();
        var providerEvidence = SampleProviderEvidence(action);

        var result = new EvidenceAdmissionRuntime().Admit(providerEvidence, action);

        Assert.True(result.Admitted);
        Assert.NotNull(result.Evidence);
        Assert.Equal(providerEvidence.Id, result.Evidence.EvidenceId);
        Assert.Equal(action.Id, result.Evidence.ActionId);
        Assert.Equal(EvidenceStatus.Admitted, result.Evidence.EvidenceStatus);
        Assert.Equal(action.CorrelationId, result.Evidence.CorrelationId);
        Assert.Equal(CanonicalEvidenceHasher.Compute(result.Evidence), result.Evidence.EvidenceHash.Value);
    }

    [Fact]
    public void AdmissionRuntime_MustRejectInvalidOwnershipIdentifiersTimestampsAndReferences()
    {
        var action = SampleAction();
        var wrongAction = action with { Id = new PaymentActionId(Guid.NewGuid()) };
        var providerEvidence = ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.NewGuid()),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference("provider-reference"),
            new EvidenceSource("provider-source"),
            new EvidenceTimestamp(action.CreatedUtc.AddMinutes(-1)),
            new EvidenceHash("provider-hash"),
            new ProviderPayloadReference("payload-reference"),
            new ActionCorrelationId("wrong-correlation"));

        var result = new EvidenceAdmissionRuntime().Admit(providerEvidence, wrongAction);

        Assert.False(result.Admitted);
        Assert.Null(result.Evidence);
        Assert.Contains(result.Reasons, reason => reason.Value == "OWNERSHIP_INVALID");
        Assert.Contains(result.Reasons, reason => reason.Value == "TIMESTAMP_INVALID");
        Assert.Contains(result.Reasons, reason => reason.Value == "CORRELATION_INVALID");
    }

    [Fact]
    public void VerificationRuntime_MustProduceVerifiedEvidenceAndVerificationResult()
    {
        var action = SampleAction();
        var admitted = Admit(action);

        var verified = new EvidenceVerificationRuntime().Verify(admitted, new EvidenceVerificationContext(action));

        Assert.Equal(admitted.EvidenceId, verified.EvidenceId);
        Assert.Equal(VerificationStatus.Verified, verified.VerificationResult.Status);
        Assert.True(verified.VerificationResult.Accepted);
        Assert.Empty(verified.VerificationResult.Failures);
        Assert.Equal(verified.VerificationId, verified.VerificationResult.VerificationId);
        Assert.Equal(CanonicalEvidenceHasher.Compute(admitted), verified.EvidenceHash.Value);
    }

    [Fact]
    public void VerificationRuntime_MustDetectDuplicatesOrderingAndHashFailures()
    {
        var action = SampleAction();
        var admitted = Admit(action);
        var later = admitted with
        {
            EvidenceId = new EvidenceId(Guid.NewGuid()),
            EvidenceReference = new EvidenceReference("later-reference"),
            EvidenceTimestamp = new EvidenceTimestamp(admitted.EvidenceTimestamp.Value.AddMinutes(5))
        };
        later = later with { EvidenceHash = new EvidenceHash(CanonicalEvidenceHasher.Compute(later)) };
        var tampered = admitted with
        {
            EvidenceId = new EvidenceId(Guid.NewGuid()),
            EvidenceHash = new EvidenceHash("invalid-hash")
        };

        var verified = new EvidenceVerificationRuntime().Verify(
            tampered,
            new EvidenceVerificationContext(action, [admitted, later]));

        Assert.Equal(VerificationStatus.Failed, verified.VerificationResult.Status);
        Assert.Contains(VerificationFailure.DuplicateEvidence, verified.VerificationResult.Failures);
        Assert.Contains(VerificationFailure.OrderingInvalid, verified.VerificationResult.Failures);
        Assert.Contains(VerificationFailure.HashInvalid, verified.VerificationResult.Failures);
    }

    [Fact]
    public void Factories_MustConstructOnlyAndEnforceInvariants()
    {
        var action = SampleAction();
        var admitted = Admit(action);
        var trace = new VerificationTrace(new VerificationTraceId("trace"));
        var result = new VerificationResult(
            admitted.EvidenceId,
            new VerificationId(Guid.NewGuid()),
            VerificationStatus.Verified,
            Array.Empty<VerificationFailure>(),
            Array.Empty<VerificationReason>(),
            trace);

        var verified = VerifiedEvidenceFactory.Create(
            admitted.EvidenceId,
            result.VerificationId,
            new VerificationTimestamp(DateTimeOffset.Parse("2026-07-10T10:01:00Z")),
            new VerificationHash(admitted.EvidenceHash.Value),
            result,
            trace);

        Assert.Equal(admitted.EvidenceId, verified.EvidenceId);
        Assert.Throws<ArgumentException>(() => AdmittedEvidenceFactory.Create(
            new EvidenceId(Guid.Empty),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Admitted,
            new EvidenceReference("reference"),
            new EvidenceSource("source"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-10T10:00:01Z")),
            new EvidenceHash("hash"),
            new ProviderPayloadReference("payload"),
            action.CorrelationId,
            new AdmissionReference("admission")));
    }

    [Fact]
    public void VerificationObjects_MustUseInitOnlyProperties()
    {
        var objectTypes = new[]
        {
            typeof(AdmittedEvidence),
            typeof(VerifiedEvidence),
            typeof(VerificationResult),
            typeof(VerificationTrace),
            typeof(VerificationMetadata)
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
    public void VerificationRuntimeSource_MustRemainProviderNeutralAndDecisionFree()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentEvidenceVerificationRuntime.cs");
        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "ManualWallet",
            "Vodafone",
            "PaymentDecision",
            "PaymentSessionFactory",
            "IPaymentPolicyEngine",
            "DbContext",
            "EntityFramework",
            "MigrationBuilder",
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

    private static PaymentAction SampleAction()
    {
        var created = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentSessionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("action-reference"),
            new ActionCorrelationId("correlation"),
            created,
            created);
    }

    private static ProviderEvidence SampleProviderEvidence(PaymentAction action)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            action.Id,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference("provider-reference"),
            new EvidenceSource("provider-source"),
            new EvidenceTimestamp(action.CreatedUtc.AddSeconds(1)),
            new EvidenceHash("provider-supplied-candidate"),
            new ProviderPayloadReference("payload-reference"),
            action.CorrelationId);
    }

    private static AdmittedEvidence Admit(PaymentAction action)
    {
        var result = new EvidenceAdmissionRuntime().Admit(SampleProviderEvidence(action), action);
        Assert.NotNull(result.Evidence);
        return result.Evidence;
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
