using System.Text.Json;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobWebhookEvidenceAdapterCertificationTests
{
    private const string HmacSecret = "paymob-hmac-secret-certification";

    [Theory]
    [InlineData("TRANSACTION", true, false, "succeeded")]
    [InlineData("TRANSACTION", false, true, "pending")]
    [InlineData("CALLBACK", true, false, "succeeded")]
    public async Task EventTranslation_MustMapSupportedPaymobCallbacksToProviderEvidenceOnly(
        string eventType,
        bool success,
        bool pending,
        string externalStatus)
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload(eventType, transactionId: 456123, orderId: 987654, success, pending);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload));

        Assert.True(result.Succeeded);
        Assert.Equal("456123", result.PaymobTransactionId);
        Assert.Equal(eventType, result.PaymobEventType);
        Assert.Equal(EvidenceType.Webhook, result.ProviderEvidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, result.ProviderEvidence.Status);
        Assert.Equal("paymob:webhook:456123", result.ProviderEvidence.Reference.Value);
        Assert.Equal("paymob", result.ProviderEvidence.Source.Value);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal(eventType, result.ProviderEvidence.Metadata.Values["event_type"]);
        Assert.Equal("paymob:order:987654", result.ProviderEvidence.Metadata.Values["provider_reference"]);
        Assert.Equal("paymob:transaction:456123", result.ProviderEvidence.Metadata.Values["transaction_reference"]);
        Assert.Equal(externalStatus, result.ProviderEvidence.Metadata.Values["external_status"]);
        Assert.NotNull(result.RuntimeResult);
    }

    [Fact]
    public async Task HmacVerification_MustUseOfficialPaymobSha512FieldOrderAndRejectInvalidSignature()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456123, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());
        var request = CreateRequest(action, session, payload) with { HmacSignature = new string('0', 128) };

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("PAYMOB_WEBHOOK_HMAC_INVALID", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task CallbackQueryPayload_MustVerifyHmacAndMapToCanonicalEvidence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreateQueryPayload(transactionId: 456132, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload));

        Assert.True(result.Succeeded);
        Assert.Equal("CALLBACK", result.PaymobEventType);
        Assert.Equal("456132", result.PaymobTransactionId);
        Assert.Equal("paymob:webhook:456132", result.ProviderEvidence.Reference.Value);
        Assert.Equal("paymob:order:987654", result.ProviderEvidence.Metadata.Values["provider_reference"]);
        Assert.Equal("succeeded", result.ProviderEvidence.Metadata.Values["external_status"]);
    }

    [Fact]
    public async Task TimestampValidation_WhenConfigured_MustFailClosed()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload(
            "TRANSACTION",
            transactionId: 456124,
            orderId: 987654,
            success: true,
            pending: false,
            createdAt: "2026-07-13T10:03:00Z");
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());
        var request = CreateRequest(action, session, payload) with
        {
            TimestampTolerance = TimeSpan.FromMinutes(1),
            ReceivedAtUtc = DateTimeOffset.Parse("2026-07-13T10:10:00Z")
        };

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_WEBHOOK_TIMESTAMP_EXPIRED", result.Error!.Code);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task ReplayProtectionHook_MustRejectThroughRuntimeWithoutPersistence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456125, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());
        PaymobWebhookHookContext? observed = null;
        PaymobWebhookDetectionHook hook = (context, _) =>
        {
            observed = context;
            return ValueTask.FromResult(PaymobWebhookHookDecision.Reject("PAYMOB_WEBHOOK_REPLAY", "Replay detected."));
        };

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload) with { ReplayDetectionHook = hook });

        Assert.NotNull(observed);
        Assert.Equal("456125", observed.TransactionId);
        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_WEBHOOK_REPLAY", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task DuplicateHook_MustRejectThroughRuntimeWithoutPersistence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456126, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());
        PaymobWebhookDetectionHook hook = (_, _) =>
            ValueTask.FromResult(PaymobWebhookHookDecision.Reject("PAYMOB_WEBHOOK_DUPLICATE", "Duplicate detected."));

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload) with { DuplicateDetectionHook = hook });

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_WEBHOOK_DUPLICATE", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task EvidenceMapping_MustCarryCanonicalReferencesAndProviderMetadata()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456127, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload));

        var metadata = result.ProviderEvidence.Metadata.Values;
        Assert.Equal("456127", metadata["event_id"]);
        Assert.Equal("456127", metadata["provider_event_id"]);
        Assert.Equal("987654", metadata["provider_order_id"]);
        Assert.Equal("merchant-order-1001", metadata["provider_merchant_order_id"]);
        Assert.Equal("paymob:order:987654", metadata["provider_reference"]);
        Assert.Equal("paymob:transaction:456127", metadata["transaction_reference"]);
        Assert.Equal(action.CorrelationId.Value, metadata["correlation_id"]);
        Assert.Equal("EGP", metadata["currency"]);
        Assert.Equal("10000", metadata["amount_cents"]);
        Assert.False(string.IsNullOrWhiteSpace(metadata["event_payload_hash"]));
        Assert.False(string.IsNullOrWhiteSpace(metadata["evidence_hash"]));
        Assert.Equal(result.ProviderEvidence.Hash.Value, metadata["evidence_hash"]);
    }

    [Fact]
    public async Task ProviderFailure_MustTranslateToCanonicalProviderErrorAndRecordedEvidence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456128, orderId: 987654, success: false, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.ProviderRejected, result.Error!.Type);
        Assert.Equal("PAYMOB_TRANSACTION_FAILED", result.Error.Code);
        Assert.Equal(EvidenceStatus.Recorded, result.ProviderEvidence.Status);
        Assert.Equal("PAYMOB_TRANSACTION_FAILED", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.DoesNotContain("PaymobOfficialCallbackPayload", JsonSerializer.Serialize(result.ProviderEvidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedEvent_MustTranslateToCanonicalProviderError()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TOKEN", transactionId: 456129, orderId: 987654, success: true, pending: false);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(CreateRequest(action, session, payload));

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_WEBHOOK_EVENT_UNSUPPORTED", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
    }

    [Fact]
    public async Task RuntimeDuplicateDetection_MustUseStableEvidenceIdAndHash()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var payload = CreatePayload("TRANSACTION", transactionId: 456130, orderId: 987654, success: true, pending: false);
        var request = CreateRequest(action, session, payload);
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var first = await adapter.ProcessAsync(request);
        var admitted = new EvidenceAdmissionRuntime().Admit(first.ProviderEvidence, action).Evidence!;
        var second = await adapter.ProcessAsync(request with { PriorEvidence = new[] { admitted } });

        Assert.Equal(first.ProviderEvidence.Id, second.ProviderEvidence.Id);
        Assert.Equal(first.ProviderEvidence.Hash, second.ProviderEvidence.Hash);
        Assert.Equal(RuntimeOverallStatus.Failed, second.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Verification, second.RuntimeResult.Failure!.FailureStage);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutPaymobProtocolTypes()
    {
        var request = CreateRequest(
            CreateAction(),
            CreateSession(PaymentStatus.Approved),
            CreatePayload("TRANSACTION", transactionId: 456131, orderId: 987654, success: true, pending: false));
        var adapter = new PaymobWebhookEvidenceAdapter(new PaymentRuntimeOrchestrator());

        var result = await adapter.ProcessAsync(request);
        var json = JsonSerializer.Serialize(result.ProviderEvidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.ProviderEvidence.Id, roundTrip.Id);
        Assert.Equal(result.ProviderEvidence.Reference, roundTrip.Reference);
        Assert.DoesNotContain("PaymobOfficialCallbackPayload", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"obj\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveWebhookTranslationBoundary()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("HMACSHA512", adapter, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute(", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobWebhookEvidenceAdapter, PaymobWebhookEvidenceAdapter>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
            Assert.DoesNotContain("hmac", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PaymobWebhookEvidenceAdapter", source, StringComparison.Ordinal);
        }
    }

    private static PaymobWebhookEvidenceRequest CreateRequest(
        PaymentAction action,
        PaymentSession session,
        string payload)
    {
        var callback = PaymobWebhookPayloadParser.Parse(payload);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(callback.HmacPayload, HmacSecret);
        return new PaymobWebhookEvidenceRequest(
            payload,
            signature,
            HmacSecret,
            RuntimeExecutionFactory.Create(
                new CorrelationId(action.CorrelationId.Value),
                session.Id,
                new ExecutionId(Guid.Parse("99999999-9999-5999-9999-999999999999")),
                DateTimeOffset.Parse("2026-07-13T10:04:00Z"),
                new PipelineMetadata(new Dictionary<string, string>
                {
                    ["adapter"] = "paymob-webhook"
                }),
                new ExecutionTraceId("paymob-webhook-test")),
            action,
            session);
    }

    private static string CreatePayload(
        string eventType,
        long transactionId,
        long orderId,
        bool success,
        bool pending,
        string createdAt = "2026-07-13T10:03:00Z")
    {
        var errorOccurred = success ? "false" : "true";
        var message = success ? "Approved" : "Declined";
        return $$"""
        {
          "type": "{{eventType}}",
          "obj": {
            "id": {{transactionId}},
            "pending": {{pending.ToString().ToLowerInvariant()}},
            "amount_cents": 10000,
            "success": {{success.ToString().ToLowerInvariant()}},
            "is_auth": false,
            "is_capture": false,
            "is_standalone_payment": true,
            "is_voided": false,
            "is_refunded": false,
            "is_3d_secure": true,
            "integration_id": 123456,
            "has_parent_transaction": false,
            "order": {
              "id": {{orderId}},
              "merchant_order_id": "merchant-order-1001"
            },
            "created_at": "{{createdAt}}",
            "currency": "EGP",
            "source_data": {
              "pan": "2346",
              "sub_type": "MasterCard",
              "type": "card"
            },
            "owner": 42,
            "error_occured": {{errorOccurred}},
            "data": {
              "message": "{{message}}",
              "txn_response_code": "00"
            }
          }
        }
        """;
    }

    private static string CreateQueryPayload(
        long transactionId,
        long orderId,
        bool success,
        bool pending)
    {
        var errorOccurred = success ? "false" : "true";
        return string.Join(
            '&',
            "amount_cents=10000",
            "created_at=2026-07-13T10%3A03%3A00Z",
            "currency=EGP",
            $"error_occured={errorOccurred}",
            "has_parent_transaction=false",
            $"id={transactionId}",
            "integration_id=123456",
            "is_3d_secure=true",
            "is_auth=false",
            "is_capture=false",
            "is_refunded=false",
            "is_standalone_payment=true",
            "is_voided=false",
            $"order={orderId}",
            "owner=42",
            $"pending={pending.ToString().ToLowerInvariant()}",
            "source_data.pan=2346",
            "source_data.sub_type=MasterCard",
            "source_data.type=card",
            $"success={success.ToString().ToLowerInvariant()}");
    }

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("paymob-webhook-action"),
            new ActionCorrelationId("paymob-webhook-correlation"),
            DateTimeOffset.Parse("2026-07-13T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-13T10:02:00Z"));
    }

    private static PaymentSession CreateSession(PaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
            new ProviderReference("paymob"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new PaymentReference("paymob"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-13T10:02:00Z"),
            attempts: [attempt]);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
