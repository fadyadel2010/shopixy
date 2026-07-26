using System.Text.Json;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeWebhookEvidenceAdapterCertificationTests
{
    private const string WebhookSecret = "whsec_certified_webhook_secret_123";

    [Theory]
    [InlineData("payment_intent.created")]
    [InlineData("payment_intent.processing")]
    [InlineData("payment_intent.requires_action")]
    [InlineData("payment_intent.succeeded")]
    [InlineData("payment_intent.payment_failed")]
    [InlineData("payment_intent.canceled")]
    [InlineData("charge.succeeded")]
    [InlineData("charge.refunded")]
    [InlineData("charge.dispute.created")]
    [InlineData("charge.dispute.closed")]
    [InlineData("checkout.session.completed")]
    public async Task EventTranslation_MustMapSupportedStripeEventsToProviderEvidenceOnly(string eventType)
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload(eventType, "evt_supported", ResolveObjectId(eventType));
        var request = CreateRequest(action, session, payload);
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal("evt_supported", result.StripeEventId);
        Assert.Equal(eventType, result.StripeEventType);
        Assert.Equal(EvidenceType.Webhook, result.ProviderEvidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, result.ProviderEvidence.Status);
        Assert.Equal("evt_supported", result.ProviderEvidence.Reference.Value);
        Assert.Equal("stripe", result.ProviderEvidence.Source.Value);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal(eventType, result.ProviderEvidence.Metadata.Values["event_type"]);
        Assert.Equal(ResolveObjectId(eventType), result.ProviderEvidence.Metadata.Values["stripe_object_id"]);
        Assert.NotNull(result.RuntimeResult);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SignatureValidation_MustUseOfficialStripeSdkAndRejectInvalidSignature()
    {
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());
        var request = CreateRequest(
            CreateAction(),
            CreateSession(PaymentStatus.Approved),
            CreatePayload("payment_intent.succeeded", "evt_bad_signature", "pi_bad"),
            signatureOverride: "t=1,v1=bad");

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("STRIPE_WEBHOOK_SIGNATURE_INVALID", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task ExpiredTimestamp_MustFailClosedAsCanonicalProviderError()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("payment_intent.succeeded", "evt_expired", "pi_expired");
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var request = CreateRequest(
            action,
            session,
            payload,
            timestamp: expiredTimestamp,
            toleranceSeconds: 60);
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("STRIPE_WEBHOOK_TIMESTAMP_EXPIRED", result.Error!.Code);
        Assert.Equal("stripe", result.Error.Metadata["provider"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task ReplayProtection_MustUseEventIdEvidenceHashAndDeterministicEvidenceIdForDuplicateDetection()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("payment_intent.succeeded", "evt_replay", "pi_replay");
        var request = CreateRequest(action, session, payload);
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var first = await adapter.ProcessAsync(request);
        var admitted = new EvidenceAdmissionRuntime().Admit(first.ProviderEvidence, action).Evidence!;
        var secondRequest = request with { PriorEvidence = new[] { admitted } };
        var second = await adapter.ProcessAsync(secondRequest);

        Assert.Equal(first.ProviderEvidence.Id, second.ProviderEvidence.Id);
        Assert.Equal(first.ProviderEvidence.Hash, second.ProviderEvidence.Hash);
        Assert.Equal(first.ProviderEvidence.Reference, second.ProviderEvidence.Reference);
        Assert.Equal(RuntimeOverallStatus.Failed, second.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Verification, second.RuntimeResult.Failure!.FailureStage);
        Assert.Contains(
            second.RuntimeResult.VerificationResult!.Failures,
            failure => failure == VerificationFailure.DuplicateEvidence);
    }

    [Fact]
    public async Task EvidenceMapping_MustCarryCanonicalWebhookFieldsAndProviderMetadata()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("payment_intent.succeeded", "evt_mapping", "pi_mapping", metadataCorrelation: "from-stripe-metadata");
        var request = CreateRequest(action, session, payload);
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);

        var metadata = result.ProviderEvidence.Metadata.Values;
        Assert.Equal("evt_mapping", metadata["event_id"]);
        Assert.Equal("evt_mapping", metadata["provider_event_id"]);
        Assert.Equal("payment_intent.succeeded", metadata["event_type"]);
        Assert.Equal("pi_mapping", metadata["provider_reference"]);
        Assert.Equal("pi_mapping", metadata["stripe_object_id"]);
        Assert.Equal(action.CorrelationId.Value, metadata["correlation_id"]);
        Assert.False(string.IsNullOrWhiteSpace(metadata["event_payload_hash"]));
        Assert.False(string.IsNullOrWhiteSpace(metadata["evidence_hash"]));
        Assert.Equal(result.ProviderEvidence.Hash.Value, metadata["evidence_hash"]);
        Assert.Equal(result.ProviderEvidence.Id.Value, result.ProviderEvidence.Id.Value);
        Assert.Equal("2026-06-24.dahlia", metadata["provider_metadata_api_version"]);
        Assert.Equal("False", metadata["provider_metadata_livemode"]);
        Assert.Equal("from-stripe-metadata", metadata["stripe_metadata_correlation_id"]);
    }

    [Fact]
    public async Task UnknownEvent_MustTranslateToCanonicalProviderErrorWithoutStripeExceptionLeak()
    {
        var request = CreateRequest(
            CreateAction(),
            CreateSession(PaymentStatus.Approved),
            CreatePayload("customer.created", "evt_unknown", "cus_unknown"));
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("STRIPE_WEBHOOK_EVENT_UNKNOWN", result.Error!.Code);
        Assert.DoesNotContain("StripeException", JsonSerializer.Serialize(result.Error), StringComparison.Ordinal);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutStripeTypes()
    {
        var request = CreateRequest(
            CreateAction(),
            CreateSession(PaymentStatus.Approved),
            CreatePayload("charge.refunded", "evt_serialize", "ch_serialize"));
        var adapter = new StripeWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);
        var json = JsonSerializer.Serialize(result.ProviderEvidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.ProviderEvidence.Id, roundTrip.Id);
        Assert.Equal(result.ProviderEvidence.Reference, roundTrip.Reference);
        Assert.DoesNotContain("Stripe.Event", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Charge", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveWebhookTranslationBoundary()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookEvidenceAdapter.cs");

        Assert.Contains("EventUtility.ConstructEvent", adapter, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute(", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("RefundService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentIntentCapture", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionService", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = System.IO.File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("using Stripe", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Stripe.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("StripeWebhookEvidenceAdapter", source, StringComparison.Ordinal);
        }
    }

    private static StripeWebhookEvidenceRequest CreateRequest(
        PaymentAction action,
        PaymentSession session,
        string payload,
        long? timestamp = null,
        long toleranceSeconds = StripeWebhookSignatureVerifier.DefaultToleranceSeconds,
        string? signatureOverride = null)
    {
        var signedAt = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = signatureOverride ?? CreateSignatureHeader(payload, signedAt);
        return new StripeWebhookEvidenceRequest(
            payload,
            signature,
            WebhookSecret,
            RuntimeExecutionFactory.Create(
                new CorrelationId(action.CorrelationId.Value),
                session.Id,
                new ExecutionId(Guid.Parse("99999999-9999-5999-9999-999999999999")),
                DateTimeOffset.Parse("2026-07-12T10:04:00Z"),
                new PipelineMetadata(new Dictionary<string, string>
                {
                    ["adapter"] = "stripe-webhook"
                }),
                new ExecutionTraceId("stripe-webhook-test")),
            action,
            session,
            toleranceSeconds);
    }

    private static string CreateSignatureHeader(string payload, long timestamp)
    {
        var signature = EventUtility.ComputeSignature(WebhookSecret, timestamp.ToString(), payload);
        return $"t={timestamp},v1={signature}";
    }

    private static string CreatePayload(
        string eventType,
        string eventId,
        string objectId,
        string metadataCorrelation = "canonical-webhook-correlation")
    {
        var created = DateTimeOffset.Parse("2026-07-12T10:03:00Z").ToUnixTimeSeconds();
        var objectType = eventType.StartsWith("charge.", StringComparison.Ordinal)
            ? "charge"
            : eventType.StartsWith("checkout.", StringComparison.Ordinal)
                ? "checkout.session"
                : eventType.StartsWith("customer.", StringComparison.Ordinal)
                    ? "customer"
                    : "payment_intent";

        if (eventType.StartsWith("charge.dispute.", StringComparison.Ordinal))
        {
            objectType = "dispute";
        }

        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": {{created}},
          "livemode": false,
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{objectId}}",
              "object": "{{objectType}}",
              "amount": 10000,
              "currency": "egp",
              "metadata": {
                "correlation_id": "{{metadataCorrelation}}",
                "order_reference": "order-1001"
              }
            }
          }
        }
        """;
    }

    private static string ResolveObjectId(string eventType)
    {
        if (eventType.StartsWith("charge.dispute.", StringComparison.Ordinal))
        {
            return "dp_supported";
        }

        if (eventType.StartsWith("charge.", StringComparison.Ordinal))
        {
            return "ch_supported";
        }

        if (eventType.StartsWith("checkout.", StringComparison.Ordinal))
        {
            return "cs_supported";
        }

        return "pi_supported";
    }

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("stripe-webhook-action"),
            new ActionCorrelationId("canonical-webhook-correlation"),
            DateTimeOffset.Parse("2026-07-12T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-12T10:02:00Z"));
    }

    private static PaymentSession CreateSession(PaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            new ProviderReference("stripe"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new PaymentReference("stripe"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T10:02:00Z"),
            attempts: [attempt]);
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
}
