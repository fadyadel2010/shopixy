using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeSettlementRuntimeCertificationTests
{
    [Fact]
    public void CanonicalSettlementTypesAndValueObjects_MustExist()
    {
        Assert.Contains(nameof(SettlementType.AuthorizationSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.CaptureSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.SaleSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.RefundSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.ChargebackSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.AdjustmentSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.ReservationSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.ReversalSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.ManualSettlement), Enum.GetNames<SettlementType>());
        Assert.Contains(nameof(SettlementType.OfflineCollectionSettlement), Enum.GetNames<SettlementType>());

        Assert.True(typeof(SettlementId).IsValueType);
        Assert.True(typeof(SettlementReference).IsValueType);
        Assert.True(typeof(SettlementTimestamp).IsValueType);
        Assert.True(typeof(SettlementReason).IsValueType);
        Assert.True(typeof(SettlementTraceId).IsValueType);
    }

    [Fact]
    public void PaymentSettlementRuntime_MustProduceCanonicalSettlementFromTransaction()
    {
        var transaction = SampleTransaction(TransactionType.Sale);

        var result = new PaymentSettlementRuntime().Produce(transaction);

        Assert.Equal(SettlementStatus.Pending, result.Status);
        Assert.Empty(result.Failures);
        Assert.NotNull(result.Settlement);
        Assert.IsAssignableFrom<IPaymentSettlement>(result.Settlement);
        Assert.Equal(SettlementType.SaleSettlement, result.Settlement!.SettlementType);
        Assert.Equal(new SettlementAmount(100m), result.Settlement.SettlementAmount);
        Assert.Equal("EGP", result.Settlement.Currency.Code);
        Assert.Equal(transaction.PaymentSessionId, result.Settlement.PaymentSessionId);
        Assert.Equal(transaction.TransactionId, result.Settlement.PaymentTransactionId);
    }

    [Fact]
    public void SameTransaction_MustProduceSameSettlementIdentityAndReference()
    {
        var transaction = SampleTransaction(TransactionType.Authorization);

        var first = new PaymentSettlementRuntime().Produce(transaction);
        var second = new PaymentSettlementRuntime().Produce(transaction);

        Assert.Equal(first.Settlement!.SettlementId, second.Settlement!.SettlementId);
        Assert.Equal(first.Settlement.SettlementReference, second.Settlement.SettlementReference);
    }

    [Fact]
    public void PaymentSettlementRuntime_MustNeverMutateTransaction()
    {
        var transaction = SampleTransaction(TransactionType.Capture);
        var originalType = transaction.TransactionType;
        var originalStatus = transaction.TransactionStatus;
        var originalAmount = transaction.TransactionAmount;

        var result = new PaymentSettlementRuntime().Produce(transaction);

        Assert.Equal(SettlementStatus.Pending, result.Status);
        Assert.Equal(originalType, transaction.TransactionType);
        Assert.Equal(originalStatus, transaction.TransactionStatus);
        Assert.Equal(originalAmount, transaction.TransactionAmount);
    }

    [Fact]
    public void PaymentSettlementRuntime_MustValidateReplayOwnershipAndDuplicates()
    {
        var transaction = SampleTransaction(TransactionType.Refund);
        var applied = new PaymentSettlementRuntime().Produce(transaction);
        var replayed = new PaymentSettlementRuntime().Produce(transaction, [applied.Settlement!]);
        var wrongOwner = applied.Settlement! with
        {
            SettlementId = new SettlementId(Guid.NewGuid()),
            PaymentSessionId = new PaymentSessionId(Guid.NewGuid()),
            PaymentTransactionId = new TransactionId(Guid.NewGuid())
        };
        var ownershipFailure = new PaymentSettlementRuntime().Produce(transaction, [wrongOwner]);
        var otherTransactionSettlement = applied.Settlement! with
        {
            SettlementId = new SettlementId(Guid.NewGuid()),
            PaymentTransactionId = new TransactionId(Guid.NewGuid())
        };
        var duplicatedPrior = otherTransactionSettlement with { SettlementId = new SettlementId(Guid.NewGuid()) };
        var duplicateFailure = new PaymentSettlementRuntime().Produce(transaction, [otherTransactionSettlement, duplicatedPrior]);

        Assert.Equal(SettlementStatus.Replayed, replayed.Status);
        Assert.Contains(SettlementFailure.ReplayDetected, replayed.Failures);
        Assert.Null(replayed.Settlement);

        Assert.Equal(SettlementStatus.Rejected, ownershipFailure.Status);
        Assert.Contains(SettlementFailure.OwnershipInvalid, ownershipFailure.Failures);

        Assert.Equal(SettlementStatus.Duplicate, duplicateFailure.Status);
        Assert.Contains(SettlementFailure.DuplicateSettlement, duplicateFailure.Failures);
    }

    [Fact]
    public void Factories_MustConstructOnlyAndValidateInvariants()
    {
        var settlement = PaymentSettlementFactory.Create(
            new SettlementId(Guid.NewGuid()),
            SettlementType.AuthorizationSettlement,
            SettlementStatus.Pending,
            new SettlementAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:05:00Z"),
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.NewGuid()),
            new TransactionId(Guid.NewGuid()),
            new SettlementReference("settlement-reference"),
            new SettlementMetadata(new Dictionary<string, string> { ["source"] = "certification" }));
        var trace = new SettlementTrace(new SettlementTraceId("settlement-trace"));
        var result = SettlementResultFactory.Create(
            SettlementStatus.Pending,
            settlement,
            Array.Empty<SettlementFailure>(),
            new SettlementReason("AUTHORIZED_SETTLEMENT"),
            trace);

        Assert.Equal(SettlementType.AuthorizationSettlement, settlement.SettlementType);
        Assert.Equal(SettlementStatus.Pending, result.Status);
        Assert.Throws<ArgumentException>(() => PaymentSettlementFactory.Create(
            new SettlementId(Guid.Empty),
            SettlementType.AuthorizationSettlement,
            SettlementStatus.Pending,
            new SettlementAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:05:00Z"),
            new CorrelationId("correlation"),
            new PaymentSessionId(Guid.NewGuid()),
            new TransactionId(Guid.NewGuid()),
            new SettlementReference("settlement-reference")));
    }

    [Fact]
    public void PaymentSettlement_MustReferenceTransactionAndSessionByIdentifiersOnly()
    {
        var propertyTypes = typeof(PaymentSettlement)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Contains(typeof(PaymentSessionId), propertyTypes);
        Assert.Contains(typeof(TransactionId), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentSession), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentTransaction), propertyTypes);
        Assert.DoesNotContain(typeof(PaymentDecision), propertyTypes);
        Assert.DoesNotContain(typeof(StateTransition), propertyTypes);
    }

    [Fact]
    public void SettlementObjects_MustUseInitOnlyPropertiesAndRoundTripThroughJson()
    {
        var result = new PaymentSettlementRuntime().Produce(SampleTransaction(TransactionType.Sale));
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<SettlementResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(result.Status, deserialized.Status);
        Assert.Equal(result.Settlement!.SettlementType, deserialized.Settlement!.SettlementType);
        Assert.Equal(result.Settlement.PaymentTransactionId, deserialized.Settlement.PaymentTransactionId);

        var objectTypes = new[]
        {
            typeof(PaymentSettlement),
            typeof(SettlementResult),
            typeof(SettlementTrace),
            typeof(SettlementMetadata)
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
    public void PaymentSettlementRuntimeSource_MustRemainProviderNeutralAndSettlementBoundaryOnly()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentSettlementRuntime.cs");
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
            "PaymentTransactionRuntime",
            "Accounting",
            "Ledger",
            "Bank",
            "Treasury",
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

        Assert.DoesNotContain("PaymentSession ", source, StringComparison.Ordinal);
        Assert.Contains("PaymentSessionId", source, StringComparison.Ordinal);
        Assert.Contains("PaymentTransactionId", source, StringComparison.Ordinal);
    }

    private static PaymentTransaction SampleTransaction(TransactionType type)
    {
        return PaymentTransactionFactory.Create(
            new TransactionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            type,
            TransactionStatus.Created,
            new TransactionAmount(100m),
            new Currency("EGP"),
            DateTimeOffset.Parse("2026-07-10T10:04:00Z"),
            new CorrelationId("certification-correlation"),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new DecisionId(Guid.NewGuid()),
            new TransitionId(Guid.NewGuid()),
            new TransactionMetadata(new Dictionary<string, string> { ["source"] = "certification" }));
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
