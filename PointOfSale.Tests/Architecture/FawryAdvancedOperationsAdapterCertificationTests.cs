using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryAdvancedOperationsAdapterCertificationTests
{
    [Fact]
    public async Task RefundMapping_MustTranslateCanonicalActionToOfficialFawryRefundAndEvidence()
    {
        FawryRefundRequest? captured = null;
        var adapter = Adapter(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/refund", request.RequestUri!.ToString());
            captured = JsonSerializer.Deserialize<FawryRefundRequest>(await request.Content!.ReadAsStringAsync(), JsonOptions);
            return JsonResponse(HttpStatusCode.OK, OperationResponse(orderStatus: "REFUNDED"));
        });

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.Refund, new Dictionary<string, string>
        {
            [FawryAdvancedOperationMetadataKeys.Amount] = "20.00",
            [FawryAdvancedOperationMetadataKeys.Currency] = "EGP",
            [FawryAdvancedOperationMetadataKeys.Reason] = "requested by customer"
        }), Credentials());

        Assert.True(result.Succeeded);
        Assert.Equal("merchant-code", captured!.MerchantCode);
        Assert.Equal("fawry-ref-123", captured.ReferenceNumber);
        Assert.Equal("20.00", captured.RefundAmount);
        Assert.Equal("requested by customer", captured.Reason);
        Assert.Equal(Sha("merchant-code", "fawry-ref-123", "20.00", "requested by customer", "secure-key"), captured.Signature);
        Assert.Equal(EvidenceStatus.Recorded, result.Evidence!.Status);
        Assert.Equal("Refund", result.Evidence.Metadata.Values["operation"]);
        Assert.Equal("Refunded", result.Evidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public void PartialRefundMapping_MustUseOfficialRefundAmountAndDeterministicSignature()
    {
        var request = FawryAdvancedOperationFactory.Create(CreateAction(FawryAdvancedOperation.PartialRefund, new Dictionary<string, string>
        {
            [FawryAdvancedOperationMetadataKeys.Amount] = "5.50",
            [FawryAdvancedOperationMetadataKeys.Currency] = "EGP"
        }), Credentials());

        Assert.Equal(FawryAdvancedOperation.PartialRefund, request.Operation);
        Assert.Equal("5.50", request.RefundRequest!.RefundAmount);
        Assert.Equal(Sha("merchant-code", "fawry-ref-123", "5.50", "secure-key"), request.RefundRequest.Signature);
        Assert.Equal("POST ECommerceWeb/Fawry/payments/refund", FawryAdvancedOperationCapabilityMatrix.Operations[FawryAdvancedOperation.PartialRefund].OfficialOperation);
    }

    [Fact]
    public async Task VoidMapping_MustCallOfficialCancelAuthorizationEndpoint()
    {
        FawryCancelAuthorizationRequest? captured = null;
        var adapter = Adapter(async request =>
        {
            Assert.Equal("https://atfawry.fawrystaging.com/ECommerceWeb/api/payment/cancel", request.RequestUri!.ToString());
            captured = JsonSerializer.Deserialize<FawryCancelAuthorizationRequest>(await request.Content!.ReadAsStringAsync(), JsonOptions);
            return JsonResponse(HttpStatusCode.OK, OperationResponse(orderStatus: "CANCELLED"));
        });

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.Void), Credentials());

        Assert.True(result.Succeeded);
        Assert.Equal("fawry-ref-123", captured!.MerchantRefNum);
        Assert.Equal(Sha("fawry-ref-123", "merchant-code", "secure-key"), captured.RequestSignature);
        Assert.Equal("Void", result.Evidence!.Metadata.Values["operation"]);
        Assert.Equal("Cancelled", result.Evidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public async Task CaptureMapping_MustCallOfficialCaptureEndpointWithOptionalAmount()
    {
        FawryCaptureRequest? captured = null;
        var adapter = Adapter(async request =>
        {
            Assert.Equal("https://atfawry.fawrystaging.com/ECommerceWeb/api/payment/capture", request.RequestUri!.ToString());
            captured = JsonSerializer.Deserialize<FawryCaptureRequest>(await request.Content!.ReadAsStringAsync(), JsonOptions);
            return JsonResponse(HttpStatusCode.OK, OperationResponse(orderStatus: "PAID"));
        });

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.Capture, new Dictionary<string, string>
        {
            [FawryAdvancedOperationMetadataKeys.Amount] = "30.00",
            [FawryAdvancedOperationMetadataKeys.Currency] = "EGP"
        }), Credentials());

        Assert.True(result.Succeeded);
        Assert.Equal("30.00", captured!.CaptureAmount);
        Assert.Equal(Sha("fawry-ref-123", "30.00", "merchant-code", "secure-key"), captured.RequestSignature);
        Assert.Equal("Capture", result.Evidence!.Metadata.Values["operation"]);
    }

    [Fact]
    public async Task InquiryMapping_MustCallOfficialPaymentStatusV2GetEndpoint()
    {
        var adapter = Adapter(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var uri = request.RequestUri!.ToString();
            Assert.StartsWith("https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/status/v2?", uri, StringComparison.Ordinal);
            Assert.Contains("merchantCode=merchant-code", uri, StringComparison.Ordinal);
            Assert.Contains("merchantRefNumber=fawry-ref-123", uri, StringComparison.Ordinal);
            Assert.Contains($"signature={Sha("merchant-code", "fawry-ref-123", "secure-key")}", uri, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, OperationResponse(orderStatus: "PAID", paymentStatus: "PAID")));
        });

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.TransactionInquiry), Credentials());

        Assert.True(result.Succeeded);
        Assert.Equal("TransactionInquiry", result.Evidence!.Metadata.Values["operation"]);
        Assert.Equal("Completed", result.Evidence.Metadata.Values["canonical_status"]);
    }

    [Fact]
    public async Task CapabilityMatrix_MustCertifySupportedAndUnsupportedOperations()
    {
        var adapter = Adapter(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, OperationResponse())));

        var unsupported = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.SettlementInquiry), Credentials());

        Assert.True(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.Refund));
        Assert.True(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.PartialRefund));
        Assert.True(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.Void));
        Assert.True(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.Capture));
        Assert.True(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.TransactionInquiry));
        Assert.False(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.SettlementInquiry));
        Assert.False(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.OperationStatus));
        Assert.False(FawryAdvancedOperationCapabilityMatrix.IsSupported(FawryAdvancedOperation.CancelUnpaidOrder));
        Assert.False(unsupported.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.NotSupported, unsupported.Error!.Type);
        Assert.Equal(EvidenceStatus.Rejected, unsupported.Evidence!.Status);
    }

    [Fact]
    public async Task ProviderFailures_MustReturnCanonicalProviderErrorAndRejectedEvidence()
    {
        var adapter = Adapter(_ => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, new { code = "9946", description = "Wrong Signature", signature = "secret" })));

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.Void), Credentials());

        Assert.False(result.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("FAWRY_OPERATION_HTTP_400", result.Error.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.Evidence!.Status);
        Assert.Equal("FAWRY_OPERATION_HTTP_400", result.Evidence.Metadata.Values["error_code"]);
        Assert.DoesNotContain("secure-key", JsonSerializer.Serialize(result.Error), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Serialization_MustRoundTripEvidenceWithoutOfficialRequestModels()
    {
        var adapter = Adapter(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, OperationResponse(orderStatus: "PAID"))));

        var result = await adapter.ExecuteAsync(CreateAction(FawryAdvancedOperation.Capture), Credentials());
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.DoesNotContain(nameof(FawryCaptureRequest), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(FawryRefundRequest), json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepFawryAdvancedAdapterTranslationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryAdvancedOperationsAdapter.cs");
        var runtimeFiles = Directory.GetFiles(Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"), "*.cs", SearchOption.AllDirectories);

        Assert.Contains("public interface IFawryAdvancedOperationsAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("ECommerceWeb/Fawry/payments/refund", adapter, StringComparison.Ordinal);
        Assert.Contains("ECommerceWeb/api/payment/capture", adapter, StringComparison.Ordinal);
        Assert.Contains("ECommerceWeb/api/payment/cancel", adapter, StringComparison.Ordinal);
        Assert.Contains("ECommerceWeb/Fawry/payments/status/v2", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Stripe", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Paymob", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("atfawry", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payments/refund", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static FawryAdvancedOperationsAdapter Adapter(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) =>
        new(new HttpClient(new StubHttpMessageHandler(handler)) { BaseAddress = new Uri("https://atfawry.fawrystaging.com/") });

    private static FawryPaymentCredentials Credentials() => new("merchant-code", "api-key", "secure-key");

    private static PaymentAction CreateAction(FawryAdvancedOperation operation, IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FawryAdvancedOperationMetadataKeys.Operation] = operation.ToString(),
            [FawryAdvancedOperationMetadataKeys.ProviderReference] = "fawry-ref-123",
            ["OrderReference"] = "order-advanced"
        };

        foreach (var (key, value) in extraMetadata ?? new Dictionary<string, string>())
        {
            values[key] = value;
        }

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("advanced-operation"),
            new ActionCorrelationId("advanced-correlation"),
            DateTimeOffset.Parse("2026-07-15T10:01:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-15T10:02:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(values));
    }

    private static FawryAdvancedOperationResponse OperationResponse(string orderStatus = "PAID", string? paymentStatus = null) => new()
    {
        Type = "PaymentCaptureChargeResponse",
        StatusCode = "200",
        StatusDescription = "Operation done successfully",
        FawryRefNumber = "fawry-ref-123",
        MerchantRefNum = "fawry-ref-123",
        OrderStatus = orderStatus,
        PaymentStatus = paymentStatus,
        PaymentAmount = "20.00",
        OrderAmount = "20.00",
        CurrencyCode = "EGP",
        PaymentMethod = "CARD",
        PaymentTime = "2026-07-15T10:03:00Z"
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
    }

    private static string Sha(params string[] values)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(values)))).ToLowerInvariant();
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
