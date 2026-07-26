using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeActionsAndEvidenceCertificationTests
{
    [Fact]
    public void CanonicalActionAndEvidenceTypes_MustExistInRuntimeDomain()
    {
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(PaymentAction).Namespace);
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(ProviderEvidence).Namespace);
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(PaymentActionFactory).Namespace);
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(ProviderEvidenceFactory).Namespace);

        Assert.IsAssignableFrom<IPaymentEvidence>(SampleEvidence());
    }

    [Fact]
    public void CanonicalEnumsAndValueObjects_MustCoverActionAndEvidenceModel()
    {
        Assert.Contains(nameof(PaymentActionType.Redirect), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.EmbeddedCheckout), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.SdkConfirmation), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ReferencePayment), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.QrPayment), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.WalletApproval), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ReceiptUpload), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ReceiveShipment), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ManualReview), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.StatusRefresh), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ActionRetry), Enum.GetNames<PaymentActionType>());
        Assert.Contains(nameof(PaymentActionType.ActionCancel), Enum.GetNames<PaymentActionType>());

        Assert.Contains(nameof(EvidenceType.Webhook), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.Callback), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.Receipt), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.StatusInquiry), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.DriverConfirmation), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.CarrierConfirmation), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.SettlementReport), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.RefundEvidence), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.ManualApproval), Enum.GetNames<EvidenceType>());
        Assert.Contains(nameof(EvidenceType.ProviderNotification), Enum.GetNames<EvidenceType>());

        Assert.True(typeof(PaymentActionId).IsValueType);
        Assert.True(typeof(ActionReference).IsValueType);
        Assert.True(typeof(EvidenceReference).IsValueType);
        Assert.True(typeof(EvidenceHash).IsValueType);
        Assert.True(typeof(EvidenceSource).IsValueType);
        Assert.True(typeof(EvidenceTimestamp).IsValueType);
        Assert.True(typeof(ProviderPayloadReference).IsValueType);
    }

    [Fact]
    public void Relationships_MustBeOwnedByIdentifiersOnly()
    {
        var action = SampleAction();
        var evidence = action.Evidence.Single();

        Assert.Equal(action.Id, evidence.ActionId);
        Assert.NotEqual(Guid.Empty, action.SessionId.Value);
        Assert.NotEqual(Guid.Empty, action.AttemptId.Value);
        Assert.IsAssignableFrom<IReadOnlyCollection<ProviderEvidence>>(action.Evidence);
        Assert.IsNotType<ProviderEvidence[]>(action.Evidence);

        Assert.DoesNotContain(typeof(PaymentSession), typeof(PaymentAction).GetProperties().Select(property => property.PropertyType));
        Assert.DoesNotContain(typeof(PaymentAttempt), typeof(PaymentAction).GetProperties().Select(property => property.PropertyType));
        Assert.DoesNotContain(typeof(PaymentAction), typeof(ProviderEvidence).GetProperties().Select(property => property.PropertyType));

        var evidenceProperties = typeof(ProviderEvidence).GetProperties().Select(property => property.PropertyType).ToArray();
        Assert.DoesNotContain(typeof(PaymentSession), evidenceProperties);
        Assert.DoesNotContain(typeof(PaymentSessionId), evidenceProperties);
        Assert.DoesNotContain(typeof(PaymentAttempt), evidenceProperties);
        Assert.DoesNotContain(typeof(PaymentAttemptId), evidenceProperties);
    }

    [Fact]
    public void ActionsAndEvidence_MustUseInitOnlyProperties()
    {
        var objectTypes = new[]
        {
            typeof(PaymentAction),
            typeof(ProviderEvidence),
            typeof(PaymentActionResult),
            typeof(PaymentActionMetadata),
            typeof(EvidenceMetadata)
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
    public void Factories_MustConstructObjectsWithoutRuntimeBehavior()
    {
        var action = SampleAction();
        var evidence = action.Evidence.Single();

        Assert.Equal(PaymentActionType.Redirect, action.Type);
        Assert.Equal(PaymentActionStatus.Available, action.Status);
        Assert.Equal(EvidenceType.Webhook, evidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, evidence.Status);
        Assert.Equal("provider-callback", evidence.Source.Value);
    }

    [Fact]
    public void Construction_MustEnforceIdentifiersReferencesTimestampsTypesAndOwnership()
    {
        var now = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var actionId = new PaymentActionId(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => PaymentActionFactory.Create(
            new PaymentActionId(Guid.Empty),
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentAttemptId(Guid.NewGuid()),
            PaymentActionType.Redirect,
            PaymentActionStatus.Available,
            new ActionReference("action-reference"),
            new ActionCorrelationId("correlation"),
            now,
            now));

        Assert.Throws<ArgumentException>(() => PaymentActionFactory.Create(
            actionId,
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentAttemptId(Guid.NewGuid()),
            PaymentActionType.Unknown,
            PaymentActionStatus.Available,
            new ActionReference("action-reference"),
            new ActionCorrelationId("correlation"),
            now,
            now));

        Assert.Throws<ArgumentException>(() => ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.NewGuid()),
            actionId,
            EvidenceType.Unknown,
            EvidenceStatus.Recorded,
            new EvidenceReference("evidence-reference"),
            new EvidenceSource("provider-callback"),
            new EvidenceTimestamp(now),
            new EvidenceHash("sha256"),
            new ProviderPayloadReference("payload"),
            new ActionCorrelationId("correlation")));

        var unrelatedEvidence = ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.NewGuid()),
            new PaymentActionId(Guid.NewGuid()),
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference("evidence-reference"),
            new EvidenceSource("provider-callback"),
            new EvidenceTimestamp(now),
            new EvidenceHash("sha256"),
            new ProviderPayloadReference("payload"),
            new ActionCorrelationId("correlation"));

        Assert.Throws<ArgumentException>(() => PaymentActionFactory.Create(
            actionId,
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentAttemptId(Guid.NewGuid()),
            PaymentActionType.Redirect,
            PaymentActionStatus.Available,
            new ActionReference("action-reference"),
            new ActionCorrelationId("correlation"),
            now,
            now,
            evidence: [unrelatedEvidence]));
    }

    [Fact]
    public void ActionsAndEvidence_MustRoundTripThroughJsonSerialization()
    {
        var action = SampleAction();
        var json = JsonSerializer.Serialize(action);
        var deserialized = JsonSerializer.Deserialize<PaymentAction>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(action.Id, deserialized.Id);
        Assert.Equal(action.Type, deserialized.Type);
        Assert.Equal(action.Evidence.Single().Id, deserialized.Evidence.Single().Id);
        Assert.Equal(action.Evidence.Single().ActionId, deserialized.Evidence.Single().ActionId);
    }

    [Fact]
    public void ActionAndEvidenceSources_MustRemainProviderNeutralAndInfrastructureFree()
    {
        var source = ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentIdentifiers.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeEnums.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeValueObjects.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeFactories.cs");

        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "Vodafone",
            "ManualWallet",
            "HttpClient",
            "Execute",
            "DbContext",
            "EntityFramework",
            "MigrationBuilder",
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
        var now = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var actionId = new PaymentActionId(Guid.Parse("77777777-7777-7777-7777-777777777777"));
        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("88888888-8888-8888-8888-888888888888")),
            actionId,
            EvidenceType.Webhook,
            EvidenceStatus.Recorded,
            new EvidenceReference("evidence-reference"),
            new EvidenceSource("provider-callback"),
            new EvidenceTimestamp(now),
            new EvidenceHash("sha256:abcdef"),
            new ProviderPayloadReference("payload-reference"),
            new ActionCorrelationId("correlation"),
            new EvidenceMetadata(new Dictionary<string, string> { ["evidence"] = "one" }));

        return PaymentActionFactory.Create(
            actionId,
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.Redirect,
            PaymentActionStatus.Available,
            new ActionReference("action-reference"),
            new ActionCorrelationId("correlation"),
            now,
            now,
            new PaymentActionResult(PaymentActionStatus.Available, new ReasonCode("created")),
            new PaymentActionMetadata(new Dictionary<string, string> { ["action"] = "one" }),
            [evidence]);
    }

    private static ProviderEvidence SampleEvidence() => SampleAction().Evidence.Single();

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

    private static string ReadRuntimeSource(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
