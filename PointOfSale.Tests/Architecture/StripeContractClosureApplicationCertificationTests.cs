namespace PointOfSale.Tests.Architecture;

public sealed class StripeContractClosureApplicationCertificationTests
{
    [Fact]
    public void ApplicationBoundary_MustOwnRuntimeRequestCredentialResolutionAndDtoArtifacts()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs");
        var resolver = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeTenantConfigurationResolver.cs");

        Assert.Contains("public interface IStripeRuntimeApplicationService", source, StringComparison.Ordinal);
        Assert.Contains("StripeRuntimeApplicationService", source, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantCredentialsAsync", source, StringComparison.Ordinal);
        Assert.Contains("IStripeTenantConfigurationResolver", source, StringComparison.Ordinal);
        Assert.Contains("TenantPaymentMethods", resolver, StringComparison.Ordinal);
        Assert.Contains("ConfigEncrypted", resolver, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", resolver, StringComparison.Ordinal);
        Assert.Contains("StripeExecutionFactory.CreateRequest", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteAsync", source, StringComparison.Ordinal);
        Assert.Contains("StripeRuntimeApplicationResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientSecretContract_MustRemainApplicationOwnedAndOutsideRuntime()
    {
        var appBoundary = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs");
        var dtoContract = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/IStripePaymentService.cs");
        var runtimeObjects = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs");

        Assert.Contains("ClientSecret", dtoContract, StringComparison.Ordinal);
        Assert.Contains("PublishableKey", dtoContract, StringComparison.Ordinal);
        Assert.Contains("StripePaymentIntentId", dtoContract, StringComparison.Ordinal);
        Assert.Contains("StripePaymentIntentProviderArtifact", appBoundary, StringComparison.Ordinal);
        Assert.Contains("ClientSecret", appBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", runtimeObjects, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishableKey", runtimeObjects, StringComparison.Ordinal);
        Assert.DoesNotContain("StripePaymentIntentId", runtimeObjects, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicComposition_MustPreserveControllersRoutesDtosAndMapThroughApplicationBoundary()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");

        Assert.Contains("ICommerceHubPaymentOrchestrationService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripeRuntimeApplicationService", controller, StringComparison.Ordinal);
        Assert.Contains("StripePaymentIntentResponseDto", controller, StringComparison.Ordinal);
        Assert.Contains("IStripeRuntimeApplicationService _stripeRuntimeApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("_stripeRuntimeApplicationService.CreatePaymentIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("new StripePaymentIntentResponseDto", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMapping_MustMapCanonicalProviderErrorsWithoutLeakingStripeExceptionAcrossApplicationBoundary()
    {
        var appBoundary = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs");

        Assert.Contains("CanonicalProviderError", appBoundary, StringComparison.Ordinal);
        Assert.Contains("MapProviderError", appBoundary, StringComparison.Ordinal);
        Assert.Contains("new StripeApiException(error.Message, error.Code, error.Type.ToString())", appBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("throw new StripeException", appBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureIsolation_MustKeepRuntimeAndAdaptersUnmodifiedByApplicationContract()
    {
        var runtimeBridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeBridge.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs");
        var runtimeObjects = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs");

        Assert.DoesNotContain("TenantPaymentMethod", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethod", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethod", runtimeObjects, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigEncrypted", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigEncrypted", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe.", runtimeObjects, StringComparison.Ordinal);
    }
}
