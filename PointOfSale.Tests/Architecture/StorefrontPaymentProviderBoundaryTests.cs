using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontPaymentProviderBoundaryTests
{
    [Theory]
    [InlineData("STRIPE")]
    [InlineData("PAYMOB")]
    [InlineData("FAWRY")]
    [InlineData("MANUALWALLET")]
    public async Task CanonicalGateway_DispatchesEachProviderWithoutChangingAuthority(string providerCode)
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var expected = new CanonicalPaymentSession
        {
            PaymentIntentId = Guid.NewGuid(),
            PaymentReference = $"reference-{providerCode}"
        };
        var provider = new CapturingProviderGateway(providerCode, expected);
        var gateway = new PaymentGateway(new[] { provider });
        var request = new CanonicalPaymentRequest
        {
            TenantId = tenantId,
            OrderId = orderId,
            ProviderCode = new ProviderCode(providerCode.ToLowerInvariant()),
            CheckoutToken = "checkout-token",
            CustomerId = Guid.NewGuid(),
            PromotionCalculationFailed = false,
            RequestId = "request-id",
            CorrelationId = "correlation-id"
        };

        var actual = await gateway.CreateSessionAsync(request);

        Assert.Same(expected, actual);
        Assert.Same(request, provider.CapturedRequest);
        Assert.Equal(tenantId, provider.CapturedRequest!.TenantId);
        Assert.Equal(orderId, provider.CapturedRequest.OrderId);
    }

    [Fact]
    public async Task CanonicalGateway_RejectsUnsupportedProviderBeforeAnyProviderCall()
    {
        var provider = new CapturingProviderGateway("STRIPE", new CanonicalPaymentSession());
        var gateway = new PaymentGateway(new[] { provider });

        var exception = await Assert.ThrowsAsync<PointOfSale.API.Services.Payments.PaymentProviderNotSupportedException>(() =>
            gateway.CreateSessionAsync(new CanonicalPaymentRequest
            {
                TenantId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                ProviderCode = new ProviderCode("UNKNOWN")
            }));

        Assert.Equal("PROVIDER_NOT_SUPPORTED", exception.ErrorCode);
        Assert.Null(provider.CapturedRequest);
    }

    [Fact]
    public void StorefrontAggregate_MustNotEnterCommerceHubMarketplacePaymentPipeline()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var stripeController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs");
        var paymobController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs");
        var stripeWebhook = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StripeWebhooksController.cs");
        var paymobWebhook = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymobWebhooksController.cs");

        Assert.Contains("_paymentGateway.CreateSessionAsync", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubPaymentOrchestrationService", checkout, StringComparison.Ordinal);
        Assert.Contains("_stripePaymentService.CreatePaymentIntentAsync", stripeController, StringComparison.Ordinal);
        Assert.Contains("_stripePaymentService.GetPaymentIntentStatusAsync", stripeController, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.CreatePaymentIntentAsync", paymobController, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.GetPaymentStatusAsync", paymobController, StringComparison.Ordinal);
        Assert.Contains("_stripePaymentService.ProcessWebhookAsync", stripeWebhook, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.ProcessWebhookAsync", paymobWebhook, StringComparison.Ordinal);

        foreach (var source in new[] { stripeController, paymobController, stripeWebhook, paymobWebhook })
        {
            Assert.DoesNotContain("ICommerceHubPaymentOrchestrationService", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AvailabilityAndCheckout_MustShareTheSameRuntimeReadinessAuthority()
    {
        var catalog = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var readiness = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/StorefrontPaymentRuntimeReadinessService.cs");

        Assert.Contains("IStorefrontPaymentRuntimeReadinessService", catalog, StringComparison.Ordinal);
        Assert.Contains("_runtimeReadinessService.EvaluateAsync", catalog, StringComparison.Ordinal);
        Assert.Contains("IStorefrontPaymentRuntimeReadinessService", checkout, StringComparison.Ordinal);
        Assert.Contains("_paymentRuntimeReadinessService.EvaluateAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("STRIPE_PAYMENT_INTENT_CAPABILITY_DISABLED", readiness, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_CALLBACK_CAPABILITY_DISABLED", readiness, StringComparison.Ordinal);
        Assert.Contains("FawryOperationalCapability.WebhookAuthentication", readiness, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.OperatorApproval", readiness, StringComparison.Ordinal);
    }

    private sealed class CapturingProviderGateway : IProviderPaymentGateway
    {
        private readonly CanonicalPaymentSession _response;

        public CapturingProviderGateway(string providerCode, CanonicalPaymentSession response)
        {
            ProviderCode = new ProviderCode(providerCode);
            _response = response;
        }

        public ProviderCode ProviderCode { get; }
        public CanonicalPaymentRequest? CapturedRequest { get; private set; }

        public Task<CanonicalPaymentSession> CreateSessionAsync(
            CanonicalPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            CapturedRequest = request;
            return Task.FromResult(_response);
        }
    }
}
