using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;
using StripePaymentIntent = Stripe.PaymentIntent;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeProvider1CertificationTests
{
    [Fact]
    public void CapabilityProvider_MustReportCanonicalStripeCapabilitiesOnly()
    {
        var report = new StripeCapabilityProvider().GetCapabilities();

        Assert.Equal("stripe", report.Provider);
        Assert.Contains(StripeCanonicalCapability.Authorize, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.Capture, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.Cancel, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.Refund, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.PartialRefund, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.Void, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.Tokenization, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.WebhookProcessing, report.CanonicalCapabilities);
        Assert.Contains(StripeCanonicalCapability.CapabilityReporting, report.CanonicalCapabilities);
        Assert.True(report.RuntimeCapabilities.HasFlag(ProviderCapability.ReplayProtection));
        Assert.True(report.RuntimeConstraints.HasFlag(ProviderConstraint.RequiresMessageAuthentication));
    }

    [Fact]
    public async Task StripePaymentProvider_MustDelegateThroughAdaptersWithoutRuntimeOrderOrWorkflowKnowledge()
    {
        var paymentIntentAdapter = new CapturingPaymentIntentAdapter();
        var advancedAdapter = new CapturingAdvancedOperationsAdapter();
        var provider = new StripePaymentProvider(
            paymentIntentAdapter,
            advancedAdapter,
            new StripeCapabilityProvider(),
            new StripeWebhookVerifier());

        await provider.AuthorizeAsync(CreatePaymentIntentAction());
        await provider.CaptureAsync(CreateOperationAction());
        await provider.CancelAsync(CreateOperationAction());
        await provider.RefundAsync(CreateOperationAction());
        await provider.PartialRefundAsync(CreateOperationAction());

        Assert.Equal(1, paymentIntentAdapter.CreateCalls);
        Assert.Equal(
            [
                StripeAdvancedOperation.Capture,
                StripeAdvancedOperation.Cancel,
                StripeAdvancedOperation.Refund,
                StripeAdvancedOperation.PartialRefund
            ],
            advancedAdapter.Operations);
    }

    [Fact]
    public void RequestResponseAndErrorMappers_MustTranslateWithoutLeakingStripeOutsideAdapterBoundary()
    {
        var action = CreatePaymentIntentAction();
        var request = StripeRequestMapper.ToPaymentIntentRequest(action);
        var paymentIntent = new StripePaymentIntent
        {
            Id = "pi_provider1",
            Status = "succeeded",
            Amount = 12300,
            Currency = "egp",
            Created = DateTime.SpecifyKind(DateTime.Parse("2026-07-17T10:00:00Z"), DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>()
        };

        var evidence = StripeResponseMapper.ToProviderEvidence(action, paymentIntent);
        var error = StripeErrorMapper.ToCanonicalProviderError(new StripeException(
            HttpStatusCode.PaymentRequired,
            new StripeError
            {
                Type = "card_error",
                Code = "card_declined",
                Message = "declined"
            },
            "req_provider1"));

        Assert.Equal(12300, request.Options.Amount);
        Assert.Equal("egp", request.Options.Currency);
        Assert.Equal("pi_provider1", evidence.Reference.Value);
        Assert.Equal("stripe", evidence.Source.Value);
        Assert.Equal(CanonicalProviderErrorType.ProviderRejected, error.Type);
        Assert.Equal(PaymentFailureReason.ProviderDeclined, StripeErrorMapper.ToPaymentFailureReason(error));
    }

    [Fact]
    public void WebhookVerifier_MustUseOfficialStripeSignatureVerificationAndFailClosed()
    {
        const string secret = "whsec_provider1_secret";
        var payload = """
        {
          "id": "evt_provider1",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": 1784282400,
          "livemode": false,
          "type": "payment_intent.succeeded",
          "data": {
            "object": {
              "id": "pi_provider1",
              "object": "payment_intent",
              "amount": 12300,
              "currency": "egp",
              "metadata": {}
            }
          }
        }
        """;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = EventUtility.ComputeSignature(secret, timestamp.ToString(), payload);
        var verifier = new StripeWebhookVerifier();

        var valid = verifier.Verify(new StripeWebhookVerificationRequest(
            payload,
            $"t={timestamp},v1={signature}",
            secret));
        var invalid = verifier.Verify(new StripeWebhookVerificationRequest(
            payload,
            "t=1,v1=bad",
            secret));

        Assert.True(valid.Succeeded);
        Assert.Equal("evt_provider1", valid.Event!.Id);
        Assert.False(invalid.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, invalid.Error!.Type);
    }

    [Fact]
    public void ProviderRegistration_MustRegisterCanonicalStripeProviderWithoutReplacingCanonicalRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStripePaymentIntentAdapter, CapturingPaymentIntentAdapter>();
        services.AddSingleton<IStripeAdvancedOperationsAdapter, CapturingAdvancedOperationsAdapter>();
        services.AddStripeCanonicalProvider();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<StripePaymentProvider>(provider.GetRequiredService<IStripePaymentProvider>());
        Assert.IsType<StripeCapabilityProvider>(provider.GetRequiredService<IStripeCapabilityProvider>());
        Assert.IsType<StripeWebhookVerifier>(provider.GetRequiredService<IStripeWebhookVerifier>());

        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeCanonicalProvider.cs");
        var runtimeExtension = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs");

        Assert.Contains("AddStripeCanonicalProvider()", program, StringComparison.Ordinal);
        Assert.Contains("class StripePaymentProvider", source, StringComparison.Ordinal);
        Assert.Contains("class StripeCapabilityProvider", source, StringComparison.Ordinal);
        Assert.Contains("class StripeRequestMapper", source, StringComparison.Ordinal);
        Assert.Contains("class StripeResponseMapper", source, StringComparison.Ordinal);
        Assert.Contains("class StripeErrorMapper", source, StringComparison.Ordinal);
        Assert.Contains("class StripeWebhookVerifier", source, StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton<IPaymentRuntime>", runtimeExtension, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", runtimeExtension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentProvider", runtimeExtension, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepStripeProviderOutsideForbiddenDomains()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeCanonicalProvider.cs");
        var runtimeRoot = Path.Combine(FindRepoRoot(), "PointOfSale.Core", "Payments", "Runtime");

        foreach (var runtimeFile in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            var runtime = System.IO.File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("using Stripe", runtime, StringComparison.Ordinal);
            Assert.DoesNotContain("Stripe.", runtime, StringComparison.Ordinal);
            Assert.DoesNotContain("StripePaymentProvider", runtime, StringComparison.Ordinal);
        }

        foreach (var forbidden in new[]
        {
            "Order",
            "Workflow",
            "PaymentIntentDispatcher",
            "Storefront",
            "Affiliate",
            "CommerceHub",
            "RabbitMQ",
            "MassTransit",
            "DbContext",
            "Sql"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        Assert.Contains("IStripePaymentIntentAdapter", source, StringComparison.Ordinal);
        Assert.Contains("IStripeAdvancedOperationsAdapter", source, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidence", source, StringComparison.Ordinal);
    }

    private static PaymentAction CreatePaymentIntentAction()
    {
        return CreateAction(new Dictionary<string, string>
        {
            [StripeRequestMetadataKeys.Amount] = "123.00",
            [StripeRequestMetadataKeys.Currency] = "EGP",
            [StripeRequestMetadataKeys.IdempotencyKey] = "stripe-provider-1"
        });
    }

    private static PaymentAction CreateOperationAction()
    {
        return CreateAction(new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.ProviderReference] = "pi_provider1",
            [StripeOperationMetadataKeys.Amount] = "12.00",
            [StripeOperationMetadataKeys.Currency] = "EGP"
        });
    }

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string> metadata)
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.SdkConfirmation,
            PaymentActionStatus.Completed,
            new ActionReference("stripe-provider-1"),
            new ActionCorrelationId("stripe-provider-1-correlation"),
            DateTimeOffset.Parse("2026-07-17T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-17T10:02:00Z"),
            metadata: new PaymentActionMetadata(metadata));
    }

    private static ProviderEvidence CreateEvidence(PaymentAction action, string reference)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("99999999-9999-5999-9999-999999999999")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(reference),
            new EvidenceSource("stripe"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-17T10:03:00Z")),
            new EvidenceHash("provider1-hash"),
            new ProviderPayloadReference($"stripe:test:{reference}"),
            action.CorrelationId);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private sealed class CapturingPaymentIntentAdapter : IStripePaymentIntentAdapter
    {
        public int CreateCalls { get; private set; }

        public Task<StripePaymentIntentAdapterResult> CreateAsync(
            PaymentAction action,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(StripePaymentIntentAdapterResult.Success(
                CreateEvidence(action, "pi_provider1"),
                StripeRequestMapper.ToPaymentIntentRequest(action)));
        }
    }

    private sealed class CapturingAdvancedOperationsAdapter : IStripeAdvancedOperationsAdapter
    {
        public List<StripeAdvancedOperation> Operations { get; } = [];

        public Task<StripeAdvancedOperationResult> ExecuteAsync(
            PaymentAction action,
            CancellationToken cancellationToken = default)
        {
            var request = StripeRequestMapper.ToAdvancedOperationRequest(action);
            Operations.Add(request.Operation);
            return Task.FromResult(StripeAdvancedOperationResult.Success(
                CreateEvidence(action, request.Operation.ToString()),
                request));
        }
    }
}
