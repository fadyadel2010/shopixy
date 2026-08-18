using System.Text.Json;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;
using StripePaymentIntent = Stripe.PaymentIntent;

namespace PointOfSale.Tests.Architecture;

public sealed class StripePaymentIntentAdapterCertificationTests
{
    [Fact]
    public async Task RequestMapping_MustTranslateCanonicalActionToStripePaymentIntentOptions()
    {
        var action = CreateAction(new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Amount] = "123.45",
            [StripeRequestMetadataKeys.Currency] = "USD",
            [StripeRequestMetadataKeys.Description] = "Order 1001",
            [StripeRequestMetadataKeys.CustomerReference] = "cus_123",
            [StripeRequestMetadataKeys.PaymentMethodTypes] = "card,link",
            [StripeRequestMetadataKeys.CaptureMode] = "manual",
            [StripeRequestMetadataKeys.IdempotencyKey] = "canonical-key-1",
            [StripeRequestMetadataKeys.StatementDescriptor] = "POINT SALE",
            [StripeRequestMetadataKeys.ReceiptEmail] = "customer@example.com",
            ["OrderReference"] = "order-1001",
            ["orderId"] = "11111111-2222-3333-4444-555555555555",
            ["paymentReference"] = "STR-1001"
        });
        var client = new CapturingStripePaymentIntentClient(CreateStripeIntent());
        var adapter = new StripePaymentIntentAdapter(client);

        var result = await adapter.CreateAsync(action);

        Assert.True(result.Succeeded);
        Assert.NotNull(client.Options);
        Assert.Equal(12345, client.Options.Amount);
        Assert.Equal("usd", client.Options.Currency);
        Assert.Equal("Order 1001", client.Options.Description);
        Assert.Equal("cus_123", client.Options.Customer);
        Assert.Equal(["card", "link"], client.Options.PaymentMethodTypes);
        Assert.Equal("manual", client.Options.CaptureMethod);
        Assert.Equal("POINT SALE", client.Options.StatementDescriptor);
        Assert.Equal("customer@example.com", client.Options.ReceiptEmail);
        Assert.Equal("canonical-key-1", client.RequestOptions!.IdempotencyKey);
        Assert.Equal("stripe", client.Options.Metadata["provider"]);
        Assert.Equal("order-1001", client.Options.Metadata["canonical_OrderReference"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", client.Options.Metadata["orderId"]);
        Assert.Equal("STR-1001", client.Options.Metadata["paymentReference"]);
        Assert.Equal("11111111-2222-3333-4444-555555555555", client.Options.Metadata["canonical_orderId"]);
        Assert.Equal(action.CorrelationId.Value, client.Options.Metadata["correlation_id"]);
    }

    [Fact]
    public async Task ResponseMapping_MustTranslateStripePaymentIntentToProviderEvidence()
    {
        var action = CreateAction();
        var paymentIntent = CreateStripeIntent(status: "succeeded");
        var adapter = new StripePaymentIntentAdapter(new CapturingStripePaymentIntentClient(paymentIntent));

        var result = await adapter.CreateAsync(action);

        Assert.True(result.Succeeded);
        var evidence = Assert.IsType<ProviderEvidence>(result.Evidence);
        Assert.Equal(action.Id, evidence.ActionId);
        Assert.Equal(EvidenceType.ProviderNotification, evidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, evidence.Status);
        Assert.Equal("pi_certified", evidence.Reference.Value);
        Assert.Equal("stripe", evidence.Source.Value);
        Assert.Equal(action.CorrelationId, evidence.CorrelationId);
        Assert.Equal("stripe:payment_intent:pi_certified", evidence.PayloadReference.Value);
        Assert.Equal("succeeded", evidence.Metadata.Values["provider_status"]);
        Assert.Equal("pi_certified", evidence.Metadata.Values["provider_reference"]);
        Assert.Equal("canonical-correlation", evidence.Metadata.Values["correlation_id"]);
        Assert.NotEqual(Guid.Empty, evidence.Id.Value);
        Assert.False(string.IsNullOrWhiteSpace(evidence.Hash.Value));
    }

    [Fact]
    public void IdempotencyKey_MustBeDeterministicStableAndReplaySafe()
    {
        var first = CreateAction(new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Amount] = "10.00",
            [StripeRequestMetadataKeys.Currency] = "EGP"
        });
        var second = CreateAction(new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Currency] = "EGP",
            [StripeRequestMetadataKeys.Amount] = "10.00"
        });

        var firstKey = StripeIdempotencyKey.Build(first);
        var secondKey = StripeIdempotencyKey.Build(second);

        Assert.Equal(firstKey, secondKey);
        Assert.StartsWith("stripe:pi:", firstKey.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(Guid.NewGuid().ToString("N"), firstKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutStripeTypes()
    {
        var action = CreateAction();
        var adapter = new StripePaymentIntentAdapter(new CapturingStripePaymentIntentClient(CreateStripeIntent()));

        var result = await adapter.CreateAsync(action);
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.Equal(result.Evidence.Reference, roundTrip.Reference);
        Assert.DoesNotContain("PaymentIntentCreateOptions", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe.PaymentIntent", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorTranslation_MustReturnCanonicalProviderErrorWithoutEscapingStripeBoundary()
    {
        var adapter = new StripePaymentIntentAdapter(new ThrowingStripePaymentIntentClient());

        var result = await adapter.CreateAsync(CreateAction(new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Currency] = "EGP"
        }));

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error.Type);
        Assert.Equal("STRIPE_REQUEST_INVALID", result.Error.Code);
        Assert.Equal("stripe", result.Error.Metadata["provider"]);
    }

    [Fact]
    public void OfficialSdkClient_MustUseStripePaymentIntentServiceOnly()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs");

        Assert.Contains("new PaymentIntentService(client)", source, StringComparison.Ordinal);
        Assert.Contains("_service.CreateAsync(options, requestOptions, cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChargeService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimeStripeFreeAndAdapterEvidenceOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = System.IO.File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("using Stripe", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Stripe.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentIntentService", source, StringComparison.Ordinal);
        }

        Assert.Contains("ProviderEvidence", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", adapter, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string>? metadata = null)
    {
        var values = metadata ?? new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Amount] = "100.00",
            [StripeRequestMetadataKeys.Currency] = "EGP"
        };

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.SdkConfirmation,
            PaymentActionStatus.Completed,
            new ActionReference("action-reference"),
            new ActionCorrelationId("canonical-correlation"),
            DateTimeOffset.Parse("2026-07-10T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:02:00Z"),
            metadata: new PaymentActionMetadata(values));
    }

    private static StripePaymentIntent CreateStripeIntent(string status = "requires_payment_method")
    {
        return new StripePaymentIntent
        {
            Id = "pi_certified",
            Status = status,
            Amount = 10000,
            Currency = "egp",
            Created = DateTime.SpecifyKind(DateTime.Parse("2026-07-10T10:03:00Z"), DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>
            {
                ["correlation_id"] = "canonical-correlation"
            }
        };
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return System.IO.File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private sealed class CapturingStripePaymentIntentClient : IStripePaymentIntentClient
    {
        private readonly StripePaymentIntent _paymentIntent;

        public CapturingStripePaymentIntentClient(StripePaymentIntent paymentIntent)
        {
            _paymentIntent = paymentIntent;
        }

        public PaymentIntentCreateOptions? Options { get; private set; }
        public RequestOptions? RequestOptions { get; private set; }

        public Task<StripePaymentIntent> CreateAsync(
            PaymentIntentCreateOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            Options = options;
            RequestOptions = requestOptions;
            return Task.FromResult(_paymentIntent);
        }
    }

    private sealed class ThrowingStripePaymentIntentClient : IStripePaymentIntentClient
    {
        public Task<StripePaymentIntent> CreateAsync(
            PaymentIntentCreateOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("SDK should not be called for invalid canonical request.");
        }
    }
}
