namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRemediation002RuntimeAuthorityClosureTests
{
    [Fact]
    public void CommerceHubPaymentService_MustNotOwnProviderProtocol()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var paymobAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs");

        Assert.DoesNotContain("HMACSHA512", service, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyPaymobCallbackHmac", service, StringComparison.Ordinal);
        Assert.DoesNotContain("callbackParams.TryGetValue", service, StringComparison.Ordinal);
        Assert.DoesNotContain("fieldsToHash", service, StringComparison.Ordinal);
        Assert.DoesNotContain("source_data.pan", service, StringComparison.Ordinal);

        Assert.Contains("ParseCallbackAsync", service, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderCallbackRequest", service, StringComparison.Ordinal);
        Assert.Contains("ParseCallbackAsync", paymobAdapter, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookHmacVerifier.Verify", paymobAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontCheckout_MustInitiatePaymentsThroughItsCanonicalTenantGateway()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("IPaymentGateway _paymentGateway", checkout, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("CanonicalPaymentRequest", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubPaymentOrchestrationService", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentInitiateRequestDto", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryService_MustBeProviderIndependentAndUseCanonicalStorefrontGateway()
    {
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");

        Assert.Contains("IPaymentGateway _paymentGateway", recovery, StringComparison.Ordinal);
        Assert.Contains("_paymentGateway.CreateSessionAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("CanonicalPaymentRequest", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentInitiateRequestDto", recovery, StringComparison.Ordinal);

        Assert.DoesNotContain("IStripePaymentService", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("_stripePaymentService", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("_paymobPaymentService", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StripePaymentService", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobPaymentService", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlinePaymentCreation_MustHaveSingleRuntimeAuthorityChain()
    {
        var orchestration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs");
        var runtime = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentRuntime.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("IPaymentRuntime", orchestration, StringComparison.Ordinal);
        Assert.Contains("_paymentRuntime.InitiateAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPaymentService _paymentService", runtime, StringComparison.Ordinal);
        Assert.Contains("_paymentService.InitiateAsync", runtime, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, IPaymentProviderAdapter>", service, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter", service, StringComparison.Ordinal);
        Assert.Contains(".CreateSessionAsync", service, StringComparison.Ordinal);

        Assert.Contains("ICommerceHubPaymentOrchestrationService", program, StringComparison.Ordinal);
        Assert.Contains("IPaymentRuntime", program, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPaymentService", program, StringComparison.Ordinal);
        Assert.Contains("IPaymentGateway, PointOfSale.API.Services.Payments.ProviderContracts.PaymentGateway", program, StringComparison.Ordinal);
        Assert.Contains("IProviderPaymentGateway", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderProtocol_MustRemainInsideAdapters()
    {
        var providerContract = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ProviderContracts/CanonicalPaymentGatewayContracts.cs");
        var paymobAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");

        Assert.Contains("PaymentProviderCallbackResult", providerContract, StringComparison.Ordinal);
        Assert.Contains("PaymobOptionsValidator.ResolveWebhookSecret", paymobAdapter, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookHmacVerifier.Verify", paymobAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobOptionsValidator.ResolveWebhookSecret", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobWebhookHmacVerifier.Verify", service, StringComparison.Ordinal);
    }
}
