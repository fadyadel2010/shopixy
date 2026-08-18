using System.Net;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobAdvancedOperationsAdapterCertificationTests
{
    [Fact]
    public async Task RefundMapping_MustTranslateCanonicalActionToPaymobRefundAndProviderEvidence()
    {
        var action = CreateAction(PaymobAdvancedOperation.Refund, new Dictionary<string, string>
        {
            [PaymobOperationMetadataKeys.Amount] = "20.00",
            [PaymobOperationMetadataKeys.Currency] = "EGP"
        });
        var client = new CapturingPaymobOfficialApiClient();
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(client, handoff);

        var result = await ExecuteAsync(adapter, handoff, action);

        Assert.True(result.Succeeded);
        Assert.Equal("paymob-api-key", client.AuthRequest!.ApiKey);
        Assert.Equal("123456", client.RefundRequest!.TransactionId);
        Assert.Equal(2000, client.RefundRequest.AmountCents);
        Assert.Equal("auth-token", client.RefundRequest.AuthToken);
        Assert.Equal(EvidenceType.ProviderNotification, result.Evidence!.Type);
        Assert.Equal("paymob:transaction:123456", result.Evidence.Reference.Value);
        Assert.Equal("Refund", result.Evidence.Metadata.Values["operation"]);
        Assert.Equal("refunded", result.Evidence.Metadata.Values["external_status"]);
    }

    [Fact]
    public async Task PartialRefundMapping_MustUseOfficialRefundEndpointWithAmountAndDeterministicIdempotency()
    {
        var first = CreateAction(PaymobAdvancedOperation.PartialRefund, new Dictionary<string, string>
        {
            [PaymobOperationMetadataKeys.Amount] = "5.50",
            [PaymobOperationMetadataKeys.Currency] = "EGP"
        });
        var second = CreateAction(PaymobAdvancedOperation.PartialRefund, new Dictionary<string, string>
        {
            [PaymobOperationMetadataKeys.Currency] = "EGP",
            [PaymobOperationMetadataKeys.Amount] = "5.50"
        });
        var firstRequest = PaymobOperationFactory.Create(first, Credentials());
        var secondRequest = PaymobOperationFactory.Create(second, Credentials());

        Assert.Equal(firstRequest.IdempotencyKey, secondRequest.IdempotencyKey);
        Assert.Equal(550, firstRequest.AmountRequest!.AmountCents);
        Assert.StartsWith("paymob:op:PartialRefund:", firstRequest.IdempotencyKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VoidCaptureAndInquiry_MustCallOnlyOfficialPaymobOperations()
    {
        var client = new CapturingPaymobOfficialApiClient();
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(client, handoff);

        var voidResult = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Void));
        var captureResult = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Capture, new Dictionary<string, string>
        {
            [PaymobOperationMetadataKeys.Amount] = "30.00",
            [PaymobOperationMetadataKeys.Currency] = "EGP"
        }));
        var inquiryResult = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.TransactionInquiry));

        Assert.True(voidResult.Succeeded);
        Assert.True(captureResult.Succeeded);
        Assert.True(inquiryResult.Succeeded);
        Assert.Equal("123456", client.VoidRequest!.TransactionId);
        Assert.Equal("123456", client.CaptureRequest!.TransactionId);
        Assert.Equal(3000, client.CaptureRequest.AmountCents);
        Assert.Equal("123456", client.RetrievedTransactionId);
        Assert.Equal("Void", voidResult.Evidence!.Metadata.Values["operation"]);
        Assert.Equal("Capture", captureResult.Evidence!.Metadata.Values["operation"]);
        Assert.Equal("TransactionInquiry", inquiryResult.Evidence!.Metadata.Values["operation"]);
    }

    [Fact]
    public async Task CapabilityMatrix_MustDocumentSupportedAndUnsupportedOperations()
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(new CapturingPaymobOfficialApiClient(), handoff);
        var unsupported = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.PaymentStatus));

        Assert.True(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.Refund));
        Assert.True(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.PartialRefund));
        Assert.True(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.Void));
        Assert.True(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.Capture));
        Assert.True(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.TransactionInquiry));
        Assert.False(PaymobCapabilityMatrix.IsSupported(PaymobAdvancedOperation.PaymentStatus));
        Assert.False(unsupported.Succeeded);
        Assert.Equal("PAYMOB_OPERATION_UNSUPPORTED", unsupported.Error!.Code);
    }

    [Fact]
    public async Task Validation_MustFailClosedForInvalidReferenceAndMissingAmount()
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(new CapturingPaymobOfficialApiClient(), handoff);

        var invalidReference = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Void, providerReference: "paymob:transaction:123456"));
        var missingAmount = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Capture));

        Assert.False(invalidReference.Succeeded);
        Assert.Equal("PAYMOB_OPERATION_REQUEST_INVALID", invalidReference.Error!.Code);
        Assert.False(missingAmount.Succeeded);
        Assert.Equal("PAYMOB_OPERATION_REQUEST_INVALID", missingAmount.Error!.Code);
    }

    [Fact]
    public async Task ErrorTranslation_MustReturnCanonicalProviderErrorWithoutProviderExceptionLeak()
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(
            new ThrowingPaymobOfficialApiClient(new PaymobOfficialApiException(HttpStatusCode.BadRequest, "{\"message\":\"invalid state\"}", "req-1")),
            handoff);

        var result = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Void));

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.Equal(CanonicalProviderErrorType.Validation, result.Error!.Type);
        Assert.Equal("PAYMOB_HTTP_400", result.Error.Code);
        Assert.Equal("paymob", result.Error.Metadata["provider"]);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutPaymobWireTypes()
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobAdvancedOperationsAdapter(new CapturingPaymobOfficialApiClient(), handoff);

        var result = await ExecuteAsync(adapter, handoff, CreateAction(PaymobAdvancedOperation.Void));
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.DoesNotContain(nameof(PaymobOperationResponse), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(PaymobOperationTransactionRequest), json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepPaymobAdvancedAdapterTranslationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdvancedOperationsAdapter.cs");
        var serviceRegistration = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
            Assert.DoesNotContain("acceptance/void_refund", source, StringComparison.Ordinal);
            Assert.DoesNotContain("acceptance/capture", source, StringComparison.Ordinal);
        }

        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobAdvancedOperationsAdapter, PaymobAdvancedOperationsAdapter>", serviceRegistration, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobRuntimeBridge", serviceRegistration, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction(
        PaymobAdvancedOperation operation,
        IReadOnlyDictionary<string, string>? extraMetadata = null,
        string providerReference = "123456")
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PaymobOperationMetadataKeys.Operation] = operation.ToString(),
            [PaymobOperationMetadataKeys.ProviderReference] = providerReference,
            ["tenantId"] = TenantId.ToString("D"),
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
            DateTimeOffset.Parse("2026-07-12T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-12T10:02:00Z"),
            metadata: new PaymentActionMetadata(values));
    }

    private static async Task<PaymobAdvancedOperationResult> ExecuteAsync(
        PaymobAdvancedOperationsAdapter adapter,
        PaymobAdapterCredentialHandoff handoff,
        PaymentAction action)
    {
        using var credentialLease = handoff.Begin(Credentials());
        return await adapter.ExecuteAsync(action);
    }

    private static PaymobTenantRuntimeCredentials Credentials() => new(
        TenantId,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "egy_pk_test_advanced",
        "egy_sk_test_advanced",
        "12345",
        "0123456789abcdef0123456789abcdef01234567",
        true,
        new Uri("https://api.example.test/api/paymob/webhook"),
        new Uri("https://store.example.test/payments/paymob/callback"),
        IframeId: "67890",
        ApiKey: "paymob-api-key");

    private static readonly Guid TenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static PaymobOperationResponse OperationResponse(bool success = true, bool refunded = false, bool voided = false, bool capture = true)
    {
        return new PaymobOperationResponse
        {
            Id = 123456,
            Success = success,
            IsRefunded = refunded,
            IsVoided = voided,
            IsCapture = capture,
            AmountCents = 2000,
            Currency = "EGP",
            CreatedAt = "2026-07-12T10:04:00Z",
            Order = new PaymobOfficialOrder { Id = 987654, MerchantOrderId = "order-advanced" }
        };
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

    private sealed class CapturingPaymobOfficialApiClient : IPaymobOfficialApiClient
    {
        public PaymobAuthRequest? AuthRequest { get; private set; }
        public PaymobOperationAmountRequest? RefundRequest { get; private set; }
        public PaymobOperationTransactionRequest? VoidRequest { get; private set; }
        public PaymobOperationAmountRequest? CaptureRequest { get; private set; }
        public string? RetrievedTransactionId { get; private set; }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken)
        {
            AuthRequest = request;
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(PaymobOrderRegistrationRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(PaymobPaymentKeyRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken)
        {
            RefundRequest = request;
            return Task.FromResult(OperationResponse(refunded: true));
        }

        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken)
        {
            VoidRequest = request;
            return Task.FromResult(OperationResponse(voided: true));
        }

        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken)
        {
            CaptureRequest = request;
            return Task.FromResult(OperationResponse(capture: true));
        }

        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken)
        {
            RetrievedTransactionId = transactionId;
            return Task.FromResult(OperationResponse());
        }
    }

    private sealed class ThrowingPaymobOfficialApiClient : IPaymobOfficialApiClient
    {
        private readonly Exception _exception;

        public ThrowingPaymobOfficialApiClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(PaymobOrderRegistrationRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(PaymobPaymentKeyRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw _exception;
    }
}
