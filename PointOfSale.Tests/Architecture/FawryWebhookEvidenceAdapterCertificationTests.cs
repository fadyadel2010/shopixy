using System.Globalization;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryWebhookEvidenceAdapterCertificationTests
{
    private const string SecureKey = "fawry-secure-key-certification";

    [Theory]
    [InlineData(FawryWebhookEvidenceKind.Callback, "PAID", "Completed")]
    [InlineData(FawryWebhookEvidenceKind.Webhook, "NEW", "AwaitingCustomer")]
    [InlineData(FawryWebhookEvidenceKind.Callback, "EXPIRED", "Expired")]
    public async Task OfficialCallbackAndWebhookVerification_MustMapSupportedEventsToProviderEvidenceOnly(
        FawryWebhookEvidenceKind kind,
        string orderStatus,
        string canonicalStatus)
    {
        var action = CreateAction();
        var payload = SignedPayload(orderStatus: orderStatus, requestId: "request-123", fawryRefNumber: "9990076204");
        var adapter = new FawryWebhookEvidenceAdapter();

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(payload, SecureKey, action, kind));

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal("request-123", result.FawryRequestId);
        Assert.Equal("9990076204", result.FawryRefNumber);
        Assert.Equal(orderStatus, result.OrderStatus);
        Assert.Equal(EvidenceType.Webhook, result.ProviderEvidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, result.ProviderEvidence.Status);
        Assert.Equal("fawry", result.ProviderEvidence.Source.Value);
        Assert.Equal("fawry:webhook:request-123", result.ProviderEvidence.Reference.Value);
        Assert.Equal("fawry:order:9990076204", result.ProviderEvidence.PayloadReference.Value);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal(kind.ToString(), result.ProviderEvidence.Metadata.Values["evidence_ingress"]);
        Assert.Equal($"ORDER_{orderStatus}", result.ProviderEvidence.Metadata.Values["event_type"]);
        Assert.Equal(canonicalStatus, result.ProviderEvidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public async Task OfficialSignatureVerification_MustUseFawrySha256MessageSignatureAndRejectInvalidSignature()
    {
        var payload = SignedPayloadRecord(
            "PAID",
            "request-abc",
            "9990076204",
            "merchant-1001",
            "369552233",
            "PAYATFAWRY",
            "1784111400000",
            null,
            null) with { MessageSignature = new string('0', 64) };
        var adapter = new FawryWebhookEvidenceAdapter();

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(ToJson(payload), SecureKey, CreateAction()));

        Assert.False(result.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("FAWRY_WEBHOOK_SIGNATURE_INVALID", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal("FAWRY_WEBHOOK_SIGNATURE_INVALID", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
    }

    [Fact]
    public async Task MalformedPayload_MustReturnRejectedProviderEvidenceAndCanonicalProviderError()
    {
        var adapter = new FawryWebhookEvidenceAdapter();

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest("{ not-json", SecureKey, CreateAction()));

        Assert.False(result.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("FAWRY_WEBHOOK_MALFORMED_PAYLOAD", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal("fawry", result.Error.Metadata["provider"]);
    }

    [Fact]
    public async Task UnsupportedEvent_MustRejectWithoutGuessingCanonicalStatus()
    {
        var adapter = new FawryWebhookEvidenceAdapter();

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(SignedPayload(orderStatus: "SHIPPED"), SecureKey, CreateAction()));

        Assert.False(result.Succeeded);
        Assert.Equal("FAWRY_WEBHOOK_EVENT_UNSUPPORTED", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.DoesNotContain("canonical_status", result.ProviderEvidence.Metadata.Values.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TimestampValidation_WhenOfficialPaymentTimeIsPresent_MustFailClosed()
    {
        var adapter = new FawryWebhookEvidenceAdapter();
        var request = new FawryWebhookEvidenceRequest(
            SignedPayload(orderStatus: "PAID", paymentTime: "1784111400000"),
            SecureKey,
            CreateAction(),
            timestampTolerance: TimeSpan.FromMinutes(1),
            receivedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(1784112000000));

        var result = await adapter.ProcessAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("FAWRY_WEBHOOK_TIMESTAMP_EXPIRED", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
    }

    [Fact]
    public async Task ReplayHook_MustRejectWithoutPersistenceOrRuntimeDependency()
    {
        var adapter = new FawryWebhookEvidenceAdapter();
        FawryWebhookHookContext? observed = null;
        FawryWebhookDetectionHook hook = (context, _) =>
        {
            observed = context;
            return ValueTask.FromResult(FawryWebhookHookDecision.Reject("FAWRY_WEBHOOK_REPLAY", "Replay candidate."));
        };

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(
            SignedPayload(orderStatus: "PAID", requestId: "replay-1"),
            SecureKey,
            CreateAction(),
            replayDetectionHook: hook));

        Assert.NotNull(observed);
        Assert.Equal("replay-1", observed.RequestId);
        Assert.Equal("FAWRY_WEBHOOK_REPLAY", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
    }

    [Fact]
    public async Task DuplicateHook_MustRejectWithoutPersistenceOrRuntimeDependency()
    {
        var adapter = new FawryWebhookEvidenceAdapter();
        FawryWebhookDetectionHook hook = (_, _) =>
            ValueTask.FromResult(FawryWebhookHookDecision.Reject("FAWRY_WEBHOOK_DUPLICATE", "Duplicate candidate."));

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(
            SignedPayload(orderStatus: "PAID"),
            SecureKey,
            CreateAction(),
            duplicateDetectionHook: hook));

        Assert.False(result.Succeeded);
        Assert.Equal("FAWRY_WEBHOOK_DUPLICATE", result.Error!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
    }

    [Fact]
    public async Task CanonicalProviderErrorMapping_MustCarryProviderFailureAsRecordedEvidence()
    {
        var adapter = new FawryWebhookEvidenceAdapter();

        var result = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(
            SignedPayload(orderStatus: "FAILED", failureErrorCode: 9965, failureReason: "Wrong Signature"),
            SecureKey,
            CreateAction()));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.ProviderRejected, result.Error!.Type);
        Assert.Equal("FAWRY_TRANSACTION_FAILED", result.Error.Code);
        Assert.Equal(EvidenceStatus.Recorded, result.ProviderEvidence.Status);
        Assert.Equal("FAWRY_TRANSACTION_FAILED", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal("9965", result.ProviderEvidence.Metadata.Values["failure_error_code"]);
    }

    [Fact]
    public async Task EvidenceMapping_MustCarryStableReferencesHashesAndNoSensitivePayloadOrSignature()
    {
        var adapter = new FawryWebhookEvidenceAdapter();
        var payload = SignedPayload(orderStatus: "PAID", requestId: "stable-1", fawryRefNumber: "9990076205");

        var first = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(payload, SecureKey, CreateAction()));
        var second = await adapter.ProcessAsync(new FawryWebhookEvidenceRequest(payload, SecureKey, CreateAction()));
        var json = JsonSerializer.Serialize(first.ProviderEvidence);

        Assert.Equal(first.ProviderEvidence.Id, second.ProviderEvidence.Id);
        Assert.Equal(first.ProviderEvidence.Hash, second.ProviderEvidence.Hash);
        Assert.Equal(first.ProviderEvidence.Hash.Value, first.ProviderEvidence.Metadata.Values["evidence_hash"]);
        Assert.False(string.IsNullOrWhiteSpace(first.ProviderEvidence.Metadata.Values["event_payload_hash"]));
        Assert.DoesNotContain("messageSignature", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecureKey, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchitectureIsolation_MustPreserveEvidenceAdapterBoundaries()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookEvidenceAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("public interface IFawryWebhookEvidenceAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("ComputeMessageSignature", adapter, StringComparison.Ordinal);
        Assert.Contains("messageSignature", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("CanonicalProviderError", adapter, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookDetectionHook", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeResult", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("_orchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IConfiguration", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ILogger", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Stripe", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Paymob", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fawry", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new PaymentSessionId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new PaymentAttemptId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Completed,
            new ActionReference("canonical-action-reference"),
            new ActionCorrelationId("fawry-webhook-correlation"),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [FawryRequestMetadataKeys.Amount] = "350.50",
                [FawryRequestMetadataKeys.Currency] = "EGP"
            }));
    }

    private static string SignedPayload(
        string orderStatus,
        string requestId = "request-abc",
        string fawryRefNumber = "9990076204",
        string merchantRefNumber = "merchant-1001",
        string paymentReferenceNumber = "369552233",
        string paymentMethod = "PAYATFAWRY",
        string paymentTime = "1784111400000",
        int? failureErrorCode = null,
        string? failureReason = null)
    {
        return ToJson(SignedPayloadRecord(
            orderStatus,
            requestId,
            fawryRefNumber,
            merchantRefNumber,
            paymentReferenceNumber,
            paymentMethod,
            paymentTime,
            failureErrorCode,
            failureReason));
    }

    private static FawryPayload SignedPayloadRecord(
        string orderStatus,
        string requestId,
        string fawryRefNumber,
        string merchantRefNumber,
        string paymentReferenceNumber,
        string paymentMethod,
        string paymentTime,
        int? failureErrorCode,
        string? failureReason)
    {
        const decimal paymentAmount = 350.50m;
        const decimal orderAmount = 340.50m;
        var signaturePayload = string.Concat(
            fawryRefNumber,
            merchantRefNumber,
            paymentAmount.ToString("0.00", CultureInfo.InvariantCulture),
            orderAmount.ToString("0.00", CultureInfo.InvariantCulture),
            orderStatus.ToUpperInvariant(),
            paymentMethod.ToUpperInvariant(),
            paymentReferenceNumber);

        return new FawryPayload(
            requestId,
            fawryRefNumber,
            merchantRefNumber,
            paymentAmount,
            orderAmount,
            orderStatus,
            paymentMethod,
            paymentTime,
            paymentReferenceNumber,
            failureErrorCode,
            failureReason,
            Sha256(signaturePayload + SecureKey));
    }

    private static string ToJson(object value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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

    private sealed record FawryPayload(
        string RequestId,
        string FawryRefNumber,
        string MerchantRefNumber,
        decimal PaymentAmount,
        decimal OrderAmount,
        string OrderStatus,
        string PaymentMethod,
        string PaymentTime,
        string PaymentReferenceNumber,
        int? FailureErrorCode,
        string? FailureReason,
        string MessageSignature);
}
