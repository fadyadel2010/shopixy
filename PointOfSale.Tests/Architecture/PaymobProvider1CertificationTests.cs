using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobProvider1CertificationTests
{
    private const string HmacSecret = "paymob-provider-1-secret";

    [Fact]
    public void RequestMapper_MustTranslateCanonicalRuntimeRequestToPaymobDto()
    {
        var request = PaymobRequestMapper.ToCheckoutRequest(CreateCheckoutAction(), CreateCredentials());

        Assert.Equal("paymob-api-key-provider-1", request.AuthRequest.ApiKey);
        Assert.Equal(12345, request.OrderRequest.AmountCents);
        Assert.Equal("EGP", request.OrderRequest.Currency);
        Assert.Equal("merchant-provider-1", request.OrderRequest.MerchantOrderId);
        Assert.Equal(123456, request.PaymentKeyRequest.IntegrationId);
        Assert.Equal(12345, request.PaymentKeyRequest.AmountCents);
        Assert.Equal("paymob", request.CanonicalMetadata["provider"]);
    }

    [Fact]
    public void ResponseMapper_MustTranslatePaymobDtoToCanonicalEvidence()
    {
        var action = CreateCheckoutAction();
        var request = PaymobRequestMapper.ToCheckoutRequest(action, CreateCredentials());
        var response = new PaymobPaymentPreparationResponse(
            new PaymobAdapterAuthResponse("auth-token"),
            new PaymobOrderRegistrationResponse(987654, request.OrderRequest.MerchantOrderId),
            new PaymobAdapterPaymentKeyResponse("payment-token"),
            "https://accept.paymob.com/api/acceptance/iframes/456789?payment_token=payment-token");

        var evidence = PaymobResponseMapper.ToProviderEvidence(action, request, response);

        Assert.Equal("paymob", evidence.Source.Value);
        Assert.Equal("987654", evidence.Reference.Value);
        Assert.Equal("payment_key_created", evidence.Metadata.Values["external_status"]);
        Assert.Equal("paymob:order:987654:payment_key", evidence.PayloadReference.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CanonicalProviderErrorType.Authentication, PaymentFailureReason.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, CanonicalProviderErrorType.Authorization, PaymentFailureReason.AuthorizationFailed)]
    [InlineData(HttpStatusCode.BadRequest, CanonicalProviderErrorType.Validation, PaymentFailureReason.InvalidRequest)]
    [InlineData(HttpStatusCode.RequestTimeout, CanonicalProviderErrorType.Unknown, PaymentFailureReason.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, CanonicalProviderErrorType.ProviderUnavailable, PaymentFailureReason.ProviderUnavailable)]
    public void ErrorMapper_MustTranslateProviderErrorsToCanonicalErrors(
        HttpStatusCode statusCode,
        CanonicalProviderErrorType expectedType,
        PaymentFailureReason expectedReason)
    {
        var error = PaymobErrorMapper.ToCanonicalProviderError(
            new PaymobOfficialApiException(statusCode, "{\"message\":\"provider failure\"}", "req_provider_1"));

        Assert.Equal(expectedType, error.Type);
        Assert.Equal(expectedReason, PaymobErrorMapper.ToPaymentFailureReason(error));
        Assert.Equal("paymob", error.Metadata["provider"]);
    }

    [Fact]
    public void ErrorMapper_MustClassifyDeclineExpiredDuplicateAndInvalidSignature()
    {
        var decline = new CanonicalProviderError(
            CanonicalProviderErrorType.ProviderRejected,
            "PAYMOB_TRANSACTION_FAILED",
            "Declined.",
            null,
            new Dictionary<string, string> { ["provider"] = "paymob" });
        var expired = new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            "PAYMOB_WEBHOOK_TIMESTAMP_EXPIRED",
            "Expired.",
            null,
            new Dictionary<string, string> { ["provider"] = "paymob" });
        var duplicate = new CanonicalProviderError(
            CanonicalProviderErrorType.Validation,
            "PAYMOB_WEBHOOK_DUPLICATE",
            "Duplicate.",
            null,
            new Dictionary<string, string> { ["provider"] = "paymob" });
        var invalidSignature = PaymobErrorMapper.InvalidSignature();

        Assert.Equal(PaymentFailureReason.ProviderDeclined, PaymobErrorMapper.ToPaymentFailureReason(decline));
        Assert.Equal(PaymentFailureReason.InvalidState, PaymobErrorMapper.ToPaymentFailureReason(expired));
        Assert.Equal(PaymentFailureReason.InvalidState, PaymobErrorMapper.ToPaymentFailureReason(duplicate));
        Assert.Equal(PaymentFailureReason.AuthenticationFailed, PaymobErrorMapper.ToPaymentFailureReason(invalidSignature));
    }

    [Fact]
    public void CapabilityProvider_MustReportTenantSpecificConfigurationDrivenCapabilities()
    {
        var certified = new PaymobCapabilityProvider(CertifiedCapabilities()).GetCapabilities();
        var disabled = new PaymobCapabilityProvider(PaymobCapabilityModel.FoundationOnly).GetCapabilities();

        Assert.Contains(PaymobCanonicalCapability.Authorize, certified.SupportedCapabilities);
        Assert.Contains(PaymobCanonicalCapability.WebhookProcessing, certified.SupportedCapabilities);
        Assert.Contains(PaymobCanonicalCapability.Authorize, disabled.UnsupportedCapabilities.Keys);
        Assert.True(certified.RuntimeConstraints.HasFlag(ProviderConstraint.RequiresMessageAuthentication));
        Assert.False(new PaymobCapabilityProvider(PaymobCapabilityModel.FoundationOnly)
            .GetCapabilitySupport(PaymobCanonicalCapability.Authorize)
            .Supported);
    }

    [Fact]
    public void WebhookVerifier_MustVerifyHmacAndReturnCanonicalEvidenceOnly()
    {
        var payload = CreatePayload("TRANSACTION", 456123, 987654, success: true, pending: false);
        var callback = PaymobWebhookPayloadParser.Parse(payload);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(callback.HmacPayload, HmacSecret);
        var verifier = new PaymobWebhookVerifier();

        var valid = verifier.Verify(new PaymobWebhookVerifierRequest(payload, signature, HmacSecret, CreateWebhookAction()));
        var invalid = verifier.Verify(new PaymobWebhookVerifierRequest(payload, new string('0', 128), HmacSecret, CreateWebhookAction()));

        Assert.True(valid.Succeeded);
        Assert.Equal("paymob", valid.Evidence!.Source.Value);
        Assert.Equal("paymob:webhook:456123", valid.Evidence.Reference.Value);
        Assert.False(invalid.Succeeded);
        Assert.Equal(CanonicalProviderErrorType.Authentication, invalid.Error!.Type);
        Assert.Null(valid.GetType().GetProperty("RuntimeResult"));
    }

    [Fact]
    public void WebhookVerifier_MustFailClosedForDuplicateUnsupportedAndReplayUnsafePayloads()
    {
        var duplicatePayload = CreatePayload("TRANSACTION", 456124, 987654, success: true, pending: false);
        var duplicate = Verify(duplicatePayload);
        var duplicateAgain = Verify(duplicatePayload);
        var unsupported = Verify(CreatePayload("TOKEN", 456125, 987654, success: true, pending: false));

        Assert.Equal(duplicate.Evidence!.Id, duplicateAgain.Evidence!.Id);
        Assert.Equal(duplicate.Evidence.Hash, duplicateAgain.Evidence.Hash);
        Assert.False(unsupported.Succeeded);
        Assert.Equal("PAYMOB_WEBHOOK_EVENT_UNSUPPORTED", unsupported.Error!.Code);
    }

    [Fact]
    public async Task Provider_MustDelegateCheckoutOperationsInquiryAndFailuresThroughAdapters()
    {
        var paymentAdapter = new CapturingPaymobPaymentAdapter();
        var operationsAdapter = new CapturingPaymobAdvancedOperationsAdapter();
        var provider = new PaymobPaymentProvider(
            paymentAdapter,
            operationsAdapter,
            new PaymobCapabilityProvider(CertifiedCapabilities()),
            new PaymobWebhookVerifier());

        var checkout = await provider.CreateCheckoutAsync(CreateCheckoutAction());
        await provider.CaptureAsync(CreateOperationAction());
        await provider.VoidAsync(CreateOperationAction());
        await provider.RefundAsync(CreateOperationAction());
        await provider.PartialRefundAsync(CreateOperationAction());
        await provider.InquiryAsync(CreateOperationAction());

        paymentAdapter.Fail = true;
        var failed = await provider.CreateCheckoutAsync(CreateCheckoutAction());

        Assert.True(checkout.Succeeded);
        Assert.Equal(2, paymentAdapter.Calls);
        Assert.Equal(
            [
                PaymobAdvancedOperation.Capture,
                PaymobAdvancedOperation.Void,
                PaymobAdvancedOperation.Refund,
                PaymobAdvancedOperation.PartialRefund,
                PaymobAdvancedOperation.TransactionInquiry
            ],
            operationsAdapter.Operations);
        Assert.False(failed.Succeeded);
        Assert.Equal(PaymentFailureReason.ProviderUnavailable, failed.FailureReason);
    }

    [Fact]
    public void ProviderRegistration_MustAddPaymobProviderWithoutReplacingCanonicalRuntimeOrStripeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new PaymobOptions { Capabilities = CertifiedCapabilities() }));
        services.AddScoped<IPaymobOfficialApiClient, CapturingPaymobOfficialApiClient>();
        services.AddPaymobProviderAdapter();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPaymobPaymentProvider) && descriptor.ImplementationType == typeof(PaymobPaymentProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPaymobCapabilityProvider) && descriptor.ImplementationType == typeof(PaymobCapabilityProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPaymobWebhookVerifier) && descriptor.ImplementationType == typeof(PaymobWebhookVerifier));

        var runtimeExtension = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs");
        var stripeSource = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeCanonicalProvider.cs");
        var paymobSource = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobCanonicalProvider.cs");

        Assert.Contains("TryAddSingleton<IPaymentRuntime>", runtimeExtension, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", runtimeExtension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentProvider", runtimeExtension, StringComparison.Ordinal);
        Assert.Contains("class StripePaymentProvider", stripeSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobPaymentProvider", paymobSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobRequestMapper", paymobSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobResponseMapper", paymobSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobErrorMapper", paymobSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobCapabilityProvider", paymobSource, StringComparison.Ordinal);
        Assert.Contains("class PaymobWebhookVerifier", paymobSource, StringComparison.Ordinal);
    }

    private static PaymobWebhookVerifierResult Verify(string payload)
    {
        var callback = PaymobWebhookPayloadParser.Parse(payload);
        var signature = PaymobWebhookHmacVerifier.ComputeHmac(callback.HmacPayload, HmacSecret);
        return new PaymobWebhookVerifier().Verify(new PaymobWebhookVerifierRequest(payload, signature, HmacSecret, CreateWebhookAction()));
    }

    private static PaymobCapabilityModel CertifiedCapabilities() => new()
    {
        SupportsOfficialHttpsApi = true,
        SupportsHmacVerification = true,
        SupportsTenantScopedCredentials = true,
        SupportsSandboxEnvironment = true,
        SupportsProductionEnvironment = true,
        PaymentExecutionCertified = true,
        CallbackCertified = true,
        ProviderAdapterCertified = true
    };

    private static PaymobTenantRuntimeCredentials CreateCredentials() => new(
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "paymob-api-key-provider-1",
        "123456",
        "456789",
        HmacSecret,
        true);

    private static PaymentAction CreateCheckoutAction() => CreateAction(
        PaymentActionType.Redirect,
        "paymob-provider-1-checkout",
        new Dictionary<string, string>
        {
            [PaymobRequestMetadataKeys.Amount] = "123.45",
            [PaymobRequestMetadataKeys.Currency] = "EGP",
            [PaymobRequestMetadataKeys.MerchantOrderId] = "merchant-provider-1",
            [PaymobRequestMetadataKeys.ItemName] = "Provider 1 Order"
        });

    private static PaymentAction CreateOperationAction() => CreateAction(
        PaymentActionType.StatusRefresh,
        "paymob-provider-1-operation",
        new Dictionary<string, string>
        {
            [PaymobOperationMetadataKeys.ProviderReference] = "456123",
            [PaymobOperationMetadataKeys.Amount] = "123.45",
            [PaymobOperationMetadataKeys.Currency] = "EGP"
        });

    private static PaymentAction CreateWebhookAction() => CreateAction(
        PaymentActionType.StatusRefresh,
        "paymob-provider-1-webhook",
        new Dictionary<string, string>());

    private static PaymentAction CreateAction(
        PaymentActionType type,
        string reference,
        IReadOnlyDictionary<string, string> metadata)
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            type,
            PaymentActionStatus.Completed,
            new ActionReference(reference),
            new ActionCorrelationId("paymob-provider-1-correlation"),
            DateTimeOffset.Parse("2026-07-17T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-17T10:02:00Z"),
            metadata: new PaymentActionMetadata(metadata));
    }

    private static string CreatePayload(
        string eventType,
        long transactionId,
        long orderId,
        bool success,
        bool pending)
    {
        var errorOccurred = success ? "false" : "true";
        return $$"""
        {
          "type": "{{eventType}}",
          "obj": {
            "id": {{transactionId}},
            "pending": {{pending.ToString().ToLowerInvariant()}},
            "amount_cents": 12345,
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
              "merchant_order_id": "merchant-provider-1"
            },
            "created_at": "2026-07-17T10:03:00Z",
            "currency": "EGP",
            "source_data": {
              "pan": "2346",
              "sub_type": "MasterCard",
              "type": "card"
            },
            "owner": 42,
            "error_occured": {{errorOccurred}},
            "data": {
              "message": "Provider 1",
              "txn_response_code": "00"
            }
          }
        }
        """;
    }

    private static ProviderEvidence CreateEvidence(PaymentAction action, string reference)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("99999999-9999-5999-9999-999999999999")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference(reference),
            new EvidenceSource("paymob"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-17T10:03:00Z")),
            new EvidenceHash($"paymob-provider-1-{reference}"),
            new ProviderPayloadReference($"paymob:test:{reference}"),
            action.CorrelationId);
    }

    private sealed class CapturingPaymobPaymentAdapter : IPaymobPaymentAdapter
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<PaymobPaymentAdapterResult> CreateAsync(
            PaymentAction action,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail)
            {
                return Task.FromResult(PaymobPaymentAdapterResult.Failed(new CanonicalProviderError(
                    CanonicalProviderErrorType.ProviderUnavailable,
                    "PAYMOB_PROVIDER_UNAVAILABLE",
                    "Provider unavailable.",
                    null,
                    new Dictionary<string, string> { ["provider"] = "paymob" })));
            }

            var request = PaymobRequestMapper.ToCheckoutRequest(action, CreateCredentials());
            var response = new PaymobPaymentPreparationResponse(
                new PaymobAdapterAuthResponse("auth-token"),
                new PaymobOrderRegistrationResponse(987654, request.OrderRequest.MerchantOrderId),
                new PaymobAdapterPaymentKeyResponse("payment-token"),
                "https://accept.paymob.com/api/acceptance/iframes/456789?payment_token=payment-token");
            return Task.FromResult(PaymobPaymentAdapterResult.Success(
                PaymobResponseMapper.ToProviderEvidence(action, request, response),
                request,
                response));
        }
    }

    private sealed class CapturingPaymobAdvancedOperationsAdapter : IPaymobAdvancedOperationsAdapter
    {
        public List<PaymobAdvancedOperation> Operations { get; } = [];

        public Task<PaymobAdvancedOperationResult> ExecuteAsync(
            PaymentAction action,
            CancellationToken cancellationToken = default)
        {
            var request = PaymobRequestMapper.ToAdvancedOperationRequest(action, CreateCredentials());
            Operations.Add(request.Operation);
            return Task.FromResult(PaymobAdvancedOperationResult.Success(
                CreateEvidence(action, request.Operation.ToString()),
                request,
                new PaymobOperationResponse { Id = 456123, Success = true }));
        }
    }

    private sealed class CapturingPaymobOfficialApiClient : IPaymobOfficialApiClient
    {
        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(PaymobOrderRegistrationRequest request, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobOrderRegistrationResponse(987654, request.MerchantOrderId));

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(PaymobPaymentKeyRequest request, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobAdapterPaymentKeyResponse("payment-token"));

        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobOperationResponse { Id = long.Parse(request.TransactionId), Success = true });

        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobOperationResponse { Id = long.Parse(request.TransactionId), IsVoided = true });

        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobOperationResponse { Id = long.Parse(request.TransactionId), Success = true, IsCapture = true });

        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymobOperationResponse { Id = long.Parse(transactionId), Success = true });
    }
}
