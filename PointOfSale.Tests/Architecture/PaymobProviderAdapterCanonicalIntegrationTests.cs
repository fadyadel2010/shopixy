using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobProviderAdapterCanonicalIntegrationTests
{
    [Fact]
    public async Task PaymobAdapter_MustCreateHostedCheckoutThroughCanonicalAdapterContract()
    {
        var client = new CapturingPaymobOfficialApiClient();
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(client, telemetry: telemetry);

        var result = await adapter.CreateSessionAsync(CreateSessionRequest());

        Assert.Equal("PAYMOB", adapter.ProviderCode.Value);
        Assert.Equal("auth-api-key-12345", client.AuthRequest?.ApiKey);
        Assert.Equal("payment-ref-1", client.OrderRequest?.MerchantOrderId);
        Assert.Equal(12345, client.OrderRequest?.AmountCents);
        Assert.Equal("EGP", client.PaymentKeyRequest?.Currency);
        Assert.Equal(456789, client.PaymentKeyRequest?.IntegrationId);
        Assert.Equal("987654", result.ProviderIntentId);
        Assert.Equal("payment-token", result.PaymentToken);
        Assert.Contains("acceptance/iframes/123456", result.PaymentUrl, StringComparison.Ordinal);
        Assert.Contains("payment_token=payment-token", result.PaymentUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymobAdapter_MustVerifyWebhookSignatureBeforeReturningCanonicalCallback()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(new CapturingPaymobOfficialApiClient(), telemetry: telemetry);
        var payload = CreateWebhookPayload(success: true);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(
            PaymobWebhookPayloadParser.Parse(payload).HmacPayload,
            "hmac-secret-12345");

        var result = await adapter.ParseWebhookAsync(new PaymentProviderWebhookRequest(signature, payload, "corr-1"));

        Assert.Equal("TRANSACTION", result.EventType);
        Assert.Equal("111222333", result.TransactionId);
        Assert.Equal("payment-ref-1", result.ProviderReference);
        Assert.Equal("987654", result.ProviderIntentId);
        Assert.True(result.Success);
        Assert.False(result.Pending);
        Assert.Equal(12345, result.AmountMinorUnits);
        Assert.Equal("EGP", result.Currency);
    }

    [Fact]
    public async Task PaymobAdapter_MustRejectInvalidWebhookSignatureImmediately()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(new CapturingPaymobOfficialApiClient(), telemetry: telemetry);

        var exception = await Assert.ThrowsAsync<PaymentProviderAdapterException>(() =>
            adapter.ParseWebhookAsync(new PaymentProviderWebhookRequest("00", CreateWebhookPayload(success: true), "corr-1")));

        Assert.Equal("PAYMOB_WEBHOOK_HMAC_INVALID", exception.Error.Code);
    }

    [Fact]
    public async Task PaymobAdapter_MustRejectWebhookFromDifferentConfiguredMerchant()
    {
        var administration = CreateAdministrationService(merchantIdentifier: "999");
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            administration.Object);
        var payload = CreateWebhookPayload(success: true);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(
            PaymobWebhookPayloadParser.Parse(payload).HmacPayload,
            "hmac-secret-12345");

        var exception = await Assert.ThrowsAsync<PaymentProviderAdapterException>(() =>
            adapter.ParseWebhookAsync(new PaymentProviderWebhookRequest(signature, payload, "corr-merchant")));

        Assert.Equal("PAYMOB_MERCHANT_MISMATCH", exception.Error.Code);
    }

    [Fact]
    public async Task PaymobAdapter_MustAcceptWebhookFromConfiguredMerchant()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            CreateAdministrationService("123").Object);
        var payload = CreateWebhookPayload(success: true);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(
            PaymobWebhookPayloadParser.Parse(payload).HmacPayload,
            "hmac-secret-12345");

        var result = await adapter.ParseWebhookAsync(
            new PaymentProviderWebhookRequest(signature, payload, "corr-matching-merchant"));

        Assert.Equal("111222333", result.TransactionId);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task PaymobAdapter_ModernCallbackMustMatchConfiguredMidToProfileIdNotOwner()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            CreateAdministrationService("123").Object);
        var payload = CreateWebhookPayload(success: false)
            .Replace(
                "\"owner\": 123,",
                "\"profile_id\": 123, \"owner\": 999,",
                StringComparison.Ordinal);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(
            PaymobWebhookPayloadParser.Parse(payload).HmacPayload,
            "hmac-secret-12345");

        var result = await adapter.ParseWebhookAsync(
            new PaymentProviderWebhookRequest(signature, payload, "corr-modern-profile"));

        Assert.Equal("111222333", result.TransactionId);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PaymobAdapter_QueryCallbackMustExposeTerminalFailureDetails()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            CreateAdministrationService("123").Object);
        const string unsignedQuery =
            "id=111222333&pending=false&amount_cents=12345&success=false&is_auth=false&is_capture=false" +
            "&is_standalone_payment=true&is_voided=false&is_refunded=false&is_3d_secure=false" +
            "&integration_id=456789&profile_id=123&has_parent_transaction=false&order=987654" +
            "&created_at=2026-09-08T18%3A05%3A53Z&currency=EGP&owner=999&error_occured=true" +
            "&merchant_order_id=payment-ref-1&data.message=Invalid%20credentials.&source_data.pan=0007" +
            "&source_data.sub_type=MasterCard&source_data.type=card&txn_response_code=ERROR";
        var parsed = PaymobWebhookPayloadParser.Parse(unsignedQuery);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(
            parsed.HmacPayload,
            "hmac-secret-12345");
        var parameters = unsignedQuery
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part[1]),
                StringComparer.OrdinalIgnoreCase);
        parameters["hmac"] = signature;

        var result = await adapter.ParseCallbackAsync(
            new PaymentProviderCallbackRequest(parameters, "corr-declined-callback"));

        Assert.True(result.IsValid);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsPending);
        Assert.True(result.IsTerminalFailure);
        Assert.Equal(12345, result.AmountMinorUnits);
        Assert.Equal("EGP", result.Currency);
        Assert.Equal("payment-ref-1", result.ProviderReference);
        Assert.Equal("Invalid credentials.", result.FailureReason);
    }

    [Fact]
    public void PaymobAdapter_ExecutableCapabilitiesRequireCompleteSuperAdminConfiguration()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var configured = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            CreateAdministrationService("123").Object);
        var incompleteAdministration = CreateAdministrationService("123", includeApiKey: false);
        var incomplete = CreateAdapter(
            new CapturingPaymobOfficialApiClient(),
            telemetry,
            incompleteAdministration.Object);

        Assert.True(configured.Capabilities.SupportsPaymentSessionCreation);
        Assert.True(configured.Capabilities.SupportsWebhookVerification);
        Assert.True(configured.Capabilities.SupportsCallbackParsing);
        Assert.False(incomplete.Capabilities.SupportsPaymentSessionCreation);
        Assert.False(incomplete.Capabilities.SupportsWebhookVerification);
        Assert.False(incomplete.Capabilities.SupportsCallbackParsing);
    }

    [Fact]
    public async Task PaymobAdapter_MustTranslateProviderFailuresToCanonicalErrors()
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new ThrowingPaymobOfficialApiClient(new PaymobOfficialApiException(HttpStatusCode.Unauthorized, "{\"message\":\"bad key\"}", "req-1")),
            telemetry: telemetry);

        var exception = await Assert.ThrowsAsync<PaymentProviderAdapterException>(() =>
            adapter.CreateSessionAsync(CreateSessionRequest()));

        Assert.Equal(CanonicalProviderErrorType.Authentication, exception.Error.Type);
        Assert.Equal("PAYMOB_HTTP_401", exception.Error.Code);
        Assert.Equal("bad key", exception.Error.Message);
        Assert.Equal("req-1", exception.Error.ProviderRequestId);
        Assert.False(exception.OutcomeUnknown);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotAcceptable)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task PaymobAdapter_DefinitiveClientRejectionMustNotFenceOrderAsOutcomeUnknown(HttpStatusCode statusCode)
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new ThrowingPaymobOfficialApiClient(new PaymobOfficialApiException(statusCode, "{\"message\":\"rejected\"}", "req-known")),
            telemetry: telemetry,
            administrationService: CreateAdministrationService("123").Object);

        var exception = await Assert.ThrowsAsync<PaymentProviderAdapterException>(() =>
            adapter.CreateSessionAsync(CreateSessionRequest()));

        Assert.False(exception.OutcomeUnknown);
        Assert.Equal($"PAYMOB_HTTP_{(int)statusCode}", exception.Error.Code);
        Assert.Equal("req-known", exception.Error.ProviderRequestId);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PaymobAdapter_IndeterminateProviderResponseMustRetainOutcomeUnknownFence(HttpStatusCode statusCode)
    {
        using var telemetry = new PaymobOperationalTelemetry();
        var adapter = CreateAdapter(
            new ThrowingPaymobOfficialApiClient(new PaymobOfficialApiException(statusCode, "{\"message\":\"indeterminate\"}", "req-unknown")),
            telemetry: telemetry,
            administrationService: CreateAdministrationService("123").Object);

        var exception = await Assert.ThrowsAsync<PaymentProviderAdapterException>(() =>
            adapter.CreateSessionAsync(CreateSessionRequest()));

        Assert.True(exception.OutcomeUnknown);
        Assert.StartsWith("PROVIDER_SESSION_REINIT_FENCE:", exception.ProviderIntentId, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobConfiguration_MustSupportStronglyTypedSecretAndBaseUrlValidation()
    {
        var options = CreateOptions() with
        {
            HmacSecret = string.Empty,
            Secret = "alternate-secret-12345",
            BaseUrl = new Uri("https://accept.paymob.com/api/")
        };

        var result = PaymobCredentialValidator.Validate(options);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.True(result.CredentialsConfigured);
    }

    [Fact]
    public void Architecture_MustKeepPaymobProtocolInsideAdapterBoundary()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPaymentsController.cs");
        var registration = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");

        Assert.Contains("IPaymentProviderAdapter", service, StringComparison.Ordinal);
        Assert.Contains("ParseWebhookAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("auth/tokens", service, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptance/payment_keys", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HMACSHA512", service, StringComparison.Ordinal);

        Assert.Contains("class PaymobAdapter : IPaymentProviderAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("AuthenticateAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("RegisterOrderAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentKeyAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookHmacVerifier.Verify", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubMarketplaceOrder", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Accounting", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Ledger", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("VendorEarning", adapter, StringComparison.Ordinal);

        Assert.DoesNotContain("PaymobAdapter", controller, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymentProviderAdapter, PaymobAdapter>", registration, StringComparison.Ordinal);
    }

    private static PaymobAdapter CreateAdapter(
        IPaymobOfficialApiClient client,
        PaymobOperationalTelemetry? telemetry = null,
        IPaymentProviderAdministrationService? administrationService = null) =>
        new(
            client,
            Options.Create(CreateOptions()),
            new PaymobCapabilityProvider(CreateOptions().Capabilities),
            telemetry ?? new PaymobOperationalTelemetry(),
            NullLogger<PaymobAdapter>.Instance,
            administrationService);

    private static Mock<IPaymentProviderAdministrationService> CreateAdministrationService(
        string merchantIdentifier,
        bool includeApiKey = true)
    {
        var credentials = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["secretKey"] = "egy_sk_test_checkout-secret",
            ["hmacSecret"] = "hmac-secret-12345"
        };
        if (includeApiKey)
        {
            credentials["apiKey"] = "paymob-test-api-key";
        }

        var administration = new Mock<IPaymentProviderAdministrationService>();
        administration
            .Setup(service => service.GetProviderOperationalConfiguration("PAYMOB"))
            .Returns(new PaymentProviderOperationalConfiguration(
                "PAYMOB",
                "Paymob",
                true,
                "Sandbox",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["integrationId"] = "456789",
                    ["publicKey"] = "egy_pk_test_public",
                    ["merchantIdentifier"] = merchantIdentifier,
                    ["webhookUrl"] = "https://api.example.test/api/payments/paymob/webhook",
                    ["browserCallbackUrl"] = "https://market.example.test/api/commercehub/payments/paymob/callback"
                },
                credentials,
                Array.Empty<PaymentProviderCapabilityDto>(),
                new PaymentProviderHealthDto { Status = "Configured", Environment = "Sandbox" }));
        return administration;
    }

    private static CommerceHubPaymobOptions CreateOptions() => new()
    {
        Enabled = true,
        ApiKey = "auth-api-key-12345",
        IntegrationId = "456789",
        IframeId = "123456",
        HmacSecret = "hmac-secret-12345",
        Secret = string.Empty,
        BaseUrl = new Uri("https://accept.paymob.com/api/"),
        Environment = PaymobEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new PaymobFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            PaymentExecutionEnabled = true,
            CallbackExecutionEnabled = true,
            WebhookExecutionEnabled = true
        },
        Callback = new PaymobCallbackConfiguration
        {
            Configured = true,
            CallbackUrl = new Uri("https://pos.example.com/paymob/callback"),
            WebhookUrl = new Uri("https://pos.example.com/paymob/webhook")
        },
        Capabilities = new PaymobCapabilityModel
        {
            PaymentExecutionCertified = true,
            CallbackCertified = true,
            ProviderAdapterCertified = true
        }
    };

    private static PaymentProviderSessionRequest CreateSessionRequest() => new()
    {
        PaymentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        MarketplaceOrderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        OrderNumber = "CH-1001",
        ProviderReference = "payment-ref-1",
        Amount = 123.45m,
        Currency = "EGP",
        CustomerName = "Nora Ali",
        Email = "customer@example.com",
        PhoneNumber = "+201000000000",
        AddressLine = "Tahrir Square",
        City = "Cairo",
        Area = "Downtown",
        CountryCode = "EG",
        CorrelationId = "corr-1"
    };

    private static string CreateWebhookPayload(bool success) =>
        $$"""
        {
          "type": "TRANSACTION",
          "obj": {
            "id": 111222333,
            "pending": false,
            "amount_cents": 12345,
            "success": {{success.ToString().ToLowerInvariant()}},
            "is_auth": false,
            "is_capture": false,
            "is_standalone_payment": true,
            "is_voided": false,
            "is_refunded": false,
            "is_3d_secure": true,
            "integration_id": 456789,
            "has_parent_transaction": false,
            "order": { "id": 987654, "merchant_order_id": "payment-ref-1" },
            "created_at": "2026-07-19T12:00:00Z",
            "currency": "EGP",
            "source_data": { "pan": "2346", "sub_type": "MasterCard", "type": "card" },
            "owner": 123,
            "error_occured": false
          }
        }
        """;

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

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken)
        {
            AuthRequest = request;
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(PaymobOrderRegistrationRequest request, string authToken, CancellationToken cancellationToken)
        {
            OrderRequest = request;
            return Task.FromResult(new PaymobOrderRegistrationResponse(987654, request.MerchantOrderId));
        }

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(PaymobPaymentKeyRequest request, string authToken, CancellationToken cancellationToken)
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

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(PaymobOrderRegistrationRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(PaymobPaymentKeyRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobIntentionResponse> CreateIntentionAsync(PaymobIntentionRequest request, string secretKey, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw _exception;
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw _exception;
    }
}
