using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.Core.Payments.Runtime.Contracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRuntimeObjectModelCertificationTests
{
    [Fact]
    public void CanonicalRuntimeObjects_MustExistInRuntimeDomain()
    {
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(PaymentSession).Namespace);
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(PaymentAttempt).Namespace);
        Assert.Equal("PointOfSale.Core.Payments.Runtime.Domain", typeof(ProviderSession).Namespace);

        Assert.IsAssignableFrom<IPaymentSession>(SampleSession());
        Assert.IsAssignableFrom<IPaymentAttempt>(SampleSession().Attempts.Single());
    }

    [Fact]
    public void PaymentSession_MustBeAggregateRootAndOwnCollectionsByReferenceOnly()
    {
        var session = SampleSession();

        Assert.Equal(session.Id, session.Attempts.Single().SessionId);
        Assert.Equal(session.Id, session.ProviderSessions.Single().SessionId);
        Assert.Equal(session.Attempts.Single().Id, session.ProviderSessions.Single().AttemptId);
        Assert.IsAssignableFrom<IReadOnlyCollection<PaymentAttempt>>(session.Attempts);
        Assert.IsAssignableFrom<IReadOnlyCollection<ProviderSession>>(session.ProviderSessions);
        Assert.IsNotType<PaymentAttempt[]>(session.Attempts);
        Assert.IsNotType<ProviderSession[]>(session.ProviderSessions);

        Assert.DoesNotContain(typeof(PaymentSession), typeof(PaymentAttempt).GetProperties().Select(property => property.PropertyType));
        Assert.DoesNotContain(typeof(PaymentSession), typeof(ProviderSession).GetProperties().Select(property => property.PropertyType));
    }

    [Fact]
    public void RuntimeObjects_MustUseInitOnlyProperties()
    {
        var objectTypes = new[]
        {
            typeof(PaymentSession),
            typeof(PaymentAttempt),
            typeof(ProviderSession),
            typeof(Money),
            typeof(Currency),
            typeof(PaymentMetadata),
            typeof(AuditReferences)
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
    public void Factories_MustConstructValidObjectsWithoutRuntimeBehavior()
    {
        var created = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var sessionId = new PaymentSessionId(Guid.NewGuid());
        var paymentId = new PaymentId(Guid.NewGuid());
        var attemptId = new PaymentAttemptId(Guid.NewGuid());

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference("external-reference"),
            PaymentStatus.Pending,
            new FailureReason(string.Empty),
            0);

        var providerSession = ProviderSessionFactory.Create(
            new ProviderSessionId("provider-session"),
            attemptId,
            sessionId,
            new PaymentReference("provider-name"),
            new ProviderReference("external-reference"),
            new PaymentReference("external-status"),
            created,
            created);

        var session = PaymentSessionFactory.Create(
            sessionId,
            paymentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new PaymentReference("card"),
            new Money(10m, new Currency("egp")),
            PaymentStatus.Pending,
            created,
            created,
            attempts: [attempt],
            providerSessions: [providerSession]);

        Assert.Equal(PaymentStatus.Pending, session.Status);
        Assert.Equal("EGP", session.Amount.Currency.Code);
        Assert.Single(session.Attempts);
        Assert.Single(session.ProviderSessions);
    }

    [Fact]
    public void Construction_MustEnforceObjectInvariantsOnly()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => PaymentSessionFactory.Create(
            new PaymentSessionId(Guid.Empty),
            new PaymentId(Guid.NewGuid()),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new PaymentReference("card"),
            new Money(1m, new Currency("EGP")),
            PaymentStatus.Pending,
            now,
            now));

        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.NewGuid()),
            new PaymentSessionId(Guid.NewGuid()),
            new AttemptNumber(0),
            now,
            new ProviderReference("external-reference"),
            PaymentStatus.Pending,
            new FailureReason(string.Empty),
            0));

        Assert.Throws<ArgumentException>(() => ProviderSessionFactory.Create(
            new ProviderSessionId(string.Empty),
            new PaymentAttemptId(Guid.NewGuid()),
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentReference("provider-name"),
            new ProviderReference("external-reference"),
            new PaymentReference("external-status"),
            now,
            now));
    }

    [Fact]
    public void RuntimeObjects_MustRoundTripThroughJsonSerialization()
    {
        var session = SampleSession();
        var json = JsonSerializer.Serialize(session);
        var deserialized = JsonSerializer.Deserialize<PaymentSession>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(session.Id, deserialized.Id);
        Assert.Equal(session.Amount, deserialized.Amount);
        Assert.Equal(session.Attempts.Single().Id, deserialized.Attempts.Single().Id);
        Assert.Equal(session.ProviderSessions.Single().Id, deserialized.ProviderSessions.Single().Id);
    }

    [Fact]
    public void RuntimeObjectSources_MustRemainProviderNeutralAndInfrastructureFree()
    {
        var source = ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeValueObjects.cs")
            + ReadRuntimeSource("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeFactories.cs");

        var forbiddenTerms = new[]
        {
            "Stripe",
            "Paymob",
            "Fawry",
            "ManualWallet",
            "Vodafone",
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

    private static PaymentSession SampleSession()
    {
        var created = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var paymentId = new PaymentId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var attemptId = new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference("external-reference"),
            PaymentStatus.Pending,
            new FailureReason(string.Empty),
            0,
            new PaymentMetadata(new Dictionary<string, string> { ["attempt"] = "one" }));

        var providerSession = ProviderSessionFactory.Create(
            new ProviderSessionId("provider-session"),
            attemptId,
            sessionId,
            new PaymentReference("provider-name"),
            new ProviderReference("external-reference"),
            new PaymentReference("external-status"),
            created,
            created,
            new PaymentMetadata(new Dictionary<string, string> { ["provider"] = "one" }));

        return PaymentSessionFactory.Create(
            sessionId,
            paymentId,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            new PaymentReference("card"),
            new Money(100m, new Currency("EGP")),
            PaymentStatus.Pending,
            created,
            created,
            new PaymentMetadata(new Dictionary<string, string> { ["session"] = "one" }),
            new AuditReferences([new CorrelationId("correlation")]),
            [attempt],
            [providerSession]);
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

    private static string ReadRuntimeSource(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
