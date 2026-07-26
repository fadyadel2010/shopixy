using System.Net;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobPaymentAdapterCertificationTests
{
    [Fact]
    public async Task RequestMapping_MustTranslateCanonicalActionToOfficialPaymobRequests()
    {
        var action = CreateAction(new Dictionary<string, string>
        {
            [PaymobRequestMetadataKeys.Amount] = "123.45",
            [PaymobRequestMetadataKeys.Currency] = "EGP",
            [PaymobRequestMetadataKeys.MerchantOrderId] = "canonical-order-1001",
            [PaymobRequestMetadataKeys.ItemName] = "Order 1001",
            [PaymobRequestMetadataKeys.ExpirationSeconds] = "1800",
            [PaymobRequestMetadataKeys.Email] = "customer@example.com",
            [PaymobRequestMetadataKeys.FirstName] = "Nora",
            [PaymobRequestMetadataKeys.LastName] = "Ali",
            [PaymobRequestMetadataKeys.Street] = "Tahrir",
            [PaymobRequestMetadataKeys.PhoneNumber] = "+201000000000",
            [PaymobRequestMetadataKeys.City] = "Cairo",
            [PaymobRequestMetadataKeys.Country] = "EG",
            ["OrderReference"] = "order-1001"
        });
        var client = new CapturingPaymobOfficialApiClient();

        var result = await ExecuteAdapterAsync(client, action);

        Assert.True(result.Succeeded);
        Assert.NotNull(client.AuthRequest);
        Assert.Equal("paymob-api-key-12345", client.AuthRequest.ApiKey);
        Assert.NotNull(client.OrderRequest);
        Assert.Equal("auth-token", client.OrderRequest.AuthToken);
        Assert.Equal("false", client.OrderRequest.DeliveryNeeded);
        Assert.Equal(12345, client.OrderRequest.AmountCents);
        Assert.Equal("EGP", client.OrderRequest.Currency);
        Assert.Equal("canonical-order-1001", client.OrderRequest.MerchantOrderId);
        Assert.Equal("Order 1001", client.OrderRequest.Items.Single().Name);
        Assert.NotNull(client.PaymentKeyRequest);
        Assert.Equal("auth-token", client.PaymentKeyRequest.AuthToken);
        Assert.Equal("987654", client.PaymentKeyRequest.OrderId);
        Assert.Equal(12345, client.PaymentKeyRequest.AmountCents);
        Assert.Equal(1800, client.PaymentKeyRequest.Expiration);
        Assert.Equal(123456, client.PaymentKeyRequest.IntegrationId);
        Assert.True(client.PaymentKeyRequest.LockOrderWhenPaid);
        Assert.Equal("customer@example.com", client.PaymentKeyRequest.BillingData.Email);
        Assert.Equal("Nora", client.PaymentKeyRequest.BillingData.FirstName);
        Assert.Equal("Ali", client.PaymentKeyRequest.BillingData.LastName);
        Assert.Equal("order-1001", result.Request!.CanonicalMetadata["canonical_OrderReference"]);
    }

    [Fact]
    public async Task ResponseMapping_MustTranslatePaymobPreparationToProviderEvidence()
    {
        var action = CreateAction();

        var result = await ExecuteAdapterAsync(new CapturingPaymobOfficialApiClient(), action);

        Assert.True(result.Succeeded);
        var evidence = Assert.IsType<ProviderEvidence>(result.Evidence);
        Assert.Equal(action.Id, evidence.ActionId);
        Assert.Equal(EvidenceType.ProviderNotification, evidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, evidence.Status);
        Assert.Equal("987654", evidence.Reference.Value);
        Assert.Equal("paymob", evidence.Source.Value);
        Assert.Equal(action.CorrelationId, evidence.CorrelationId);
        Assert.Equal("paymob:order:987654:payment_key", evidence.PayloadReference.Value);
        Assert.Equal("payment_key_created", evidence.Metadata.Values["provider_status"]);
        Assert.Equal("987654", evidence.Metadata.Values["provider_reference"]);
        Assert.Equal("payment-token", evidence.Metadata.Values["transaction_reference"]);
        Assert.Equal("payment_key_created", evidence.Metadata.Values["external_status"]);
        Assert.Contains("acceptance/iframes/456789", evidence.Metadata.Values["iframe_url"], StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, evidence.Id.Value);
        Assert.False(string.IsNullOrWhiteSpace(evidence.Hash.Value));
    }

    [Fact]
    public void IdempotencyKey_MustUseCanonicalPaymentRuntimeIdentity()
    {
        var first = CreateAction(new Dictionary<string, string>
        {
            [PaymobRequestMetadataKeys.Amount] = "10.00",
            [PaymobRequestMetadataKeys.Currency] = "EGP"
        });
        var second = CreateAction(new Dictionary<string, string>
        {
            [PaymobRequestMetadataKeys.Currency] = "EGP",
            [PaymobRequestMetadataKeys.Amount] = "10.00"
        });

        var firstKey = PaymobIdempotencyKey.Build(first);
        var secondKey = PaymobIdempotencyKey.Build(second);

        Assert.Equal(firstKey, secondKey);
        Assert.StartsWith("paymob:", firstKey.Value, StringComparison.Ordinal);
        Assert.Contains(first.Id.Value.ToString("N"), firstKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutPaymobRequestTypes()
    {
        var result = await ExecuteAdapterAsync(new CapturingPaymobOfficialApiClient(), CreateAction());
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.Equal(result.Evidence.Reference, roundTrip.Reference);
        Assert.DoesNotContain("PaymobPaymentKeyRequest", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobOrderRegistrationRequest", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorTranslation_MustReturnCanonicalProviderErrorWithoutEscapingPaymobBoundary()
    {
        var client =
            new ThrowingPaymobOfficialApiClient(
                new PaymobOfficialApiException(HttpStatusCode.Unauthorized, "{\"message\":\"bad api key\"}", "req_123"));

        var result = await ExecuteAdapterAsync(client, CreateAction());

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Authentication, result.Error.Type);
        Assert.Equal("PAYMOB_HTTP_401", result.Error.Code);
        Assert.Equal("bad api key", result.Error.Message);
        Assert.Equal("req_123", result.Error.ProviderRequestId);
        Assert.Equal("paymob", result.Error.Metadata["provider"]);
        Assert.Equal("401", result.Error.Metadata["http_status"]);
    }

    [Fact]
    public void OfficialHttpClient_MustUseOfficialPaymobEndpointsOnly()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");

        Assert.Contains("\"auth/tokens\"", source, StringComparison.Ordinal);
        Assert.Contains("\"ecommerce/orders\"", source, StringComparison.Ordinal);
        Assert.Contains("\"acceptance/payment_keys\"", source, StringComparison.Ordinal);
        Assert.Contains("PaymobFoundationServiceCollectionExtensions.HttpClientName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Refund", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Void", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepRuntimePaymobFreeAndAdapterEvidenceOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
            Assert.DoesNotContain("payment_key", source, StringComparison.Ordinal);
            Assert.DoesNotContain("iframe", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hmac", source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ProviderEvidence", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", adapter, StringComparison.Ordinal);
        Assert.Contains("AddPaymobProviderAdapter", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobRuntimeBridge, PaymobRuntimeBridge>", program, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string>? metadata = null)
    {
        var values = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>
        {
            [PaymobRequestMetadataKeys.Amount] = "100.00",
            [PaymobRequestMetadataKeys.Currency] = "EGP",
            [PaymobRequestMetadataKeys.MerchantOrderId] = "merchant-order-1"
        }, StringComparer.Ordinal)
        {
            ["tenantId"] = TenantId.ToString("D")
        };

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.Redirect,
            PaymentActionStatus.Completed,
            new ActionReference("action-reference"),
            new ActionCorrelationId("canonical-correlation"),
            DateTimeOffset.Parse("2026-07-10T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-10T10:02:00Z"),
            metadata: new PaymentActionMetadata(values));
    }

    private static async Task<PaymobPaymentAdapterResult> ExecuteAdapterAsync(
        IPaymobOfficialApiClient client,
        PaymentAction action)
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        using var credentialLease = handoff.Begin(CreateCredentials());
        return await new PaymobPaymentAdapter(client, handoff).CreateAsync(action);
    }

    private static PaymobTenantRuntimeCredentials CreateCredentials() => new(
        TenantId,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "paymob-api-key-12345",
        "123456",
        "456789",
        "paymob-hmac-secret-12345",
        true);

    private static readonly Guid TenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

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
        public PaymobOrderRegistrationRequest? OrderRequest { get; private set; }
        public PaymobPaymentKeyRequest? PaymentKeyRequest { get; private set; }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(
            PaymobAuthRequest request,
            CancellationToken cancellationToken)
        {
            AuthRequest = request;
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
            PaymobOrderRegistrationRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            OrderRequest = request;
            return Task.FromResult(new PaymobOrderRegistrationResponse(987654, request.MerchantOrderId));
        }

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
            PaymobPaymentKeyRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            PaymentKeyRequest = request;
            return Task.FromResult(new PaymobAdapterPaymentKeyResponse("payment-token"));
        }

        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingPaymobOfficialApiClient : IPaymobOfficialApiClient
    {
        private readonly Exception _exception;

        public ThrowingPaymobOfficialApiClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(
            PaymobAuthRequest request,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
            PaymobOrderRegistrationRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
            PaymobPaymentKeyRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }

        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw _exception;
    }
}
