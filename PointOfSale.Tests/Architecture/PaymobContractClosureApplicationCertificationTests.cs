namespace PointOfSale.Tests.Architecture;

public sealed class PaymobContractClosureApplicationCertificationTests
{
    [Fact]
    public void ApplicationBoundary_MustOwnRuntimeRequestCredentialResolutionAndDtoArtifacts()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");
        var authority = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs");

        Assert.Contains("public interface IPaymobRuntimeApplicationService", source, StringComparison.Ordinal);
        Assert.Contains("PaymobRuntimeApplicationService", source, StringComparison.Ordinal);
        Assert.Contains("IPaymobTenantConfigurationResolver", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCredentialsAsync", source, StringComparison.Ordinal);
        Assert.Contains("TenantPaymentMethods", authority, StringComparison.Ordinal);
        Assert.Contains("ConfigEncrypted", authority, StringComparison.Ordinal);
        Assert.Contains("PaymentMethodConfigurationValidator.Validate", authority, StringComparison.Ordinal);
        Assert.Contains("PaymobExecutionFactory.CreateRequest", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteAsync", source, StringComparison.Ordinal);
        Assert.Contains("PaymobRuntimeApplicationResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicComposition_MustPreserveControllersRoutesDtosAndMapThroughApplicationBoundary()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var dtoContract = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/IPaymobPaymentService.cs");

        Assert.Contains("IPaymobPaymentService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubPaymentOrchestrationService", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobRuntimeApplicationService", controller, StringComparison.Ordinal);
        Assert.Contains("PaymobPaymentIntentResponseDto", controller, StringComparison.Ordinal);
        Assert.Contains("IPaymobRuntimeApplicationService _paymobRuntimeApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("_paymobRuntimeApplicationService.CreatePaymentIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("new PaymobPaymentIntentResponseDto", service, StringComparison.Ordinal);
        Assert.Contains("IframeUrl", dtoContract, StringComparison.Ordinal);
        Assert.Contains("PaymentKey", dtoContract, StringComparison.Ordinal);
        Assert.Contains("IntentionId", dtoContract, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMapping_MustMapCanonicalProviderErrorsWithoutLeakingRuntimeInternals()
    {
        var appBoundary = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");

        Assert.Contains("CanonicalProviderError", appBoundary, StringComparison.Ordinal);
        Assert.Contains("MapProviderError", appBoundary, StringComparison.Ordinal);
        Assert.Contains("new PaymobApiException(error.Message, error.Code, error.Type.ToString())", appBoundary, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_RUNTIME_OUTCOME_REJECTED", appBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("throw new PaymobOfficialApiException", appBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureIsolation_MustKeepRuntimeBridgeAndAdapterFreeOfTenantConfiguration()
    {
        var runtimeBridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var runtimeObjects = CertificationSource.ReadRepoFile("PointOfSale.Core/Payments/Runtime/Domain/PaymentRuntimeObjects.cs");

        Assert.DoesNotContain("TenantPaymentMethod", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethod", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethod", runtimeObjects, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigEncrypted", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigEncrypted", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", runtimeObjects, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_MustRegisterOnlyPaymobRuntimeApplicationServiceAsNewApplicationBoundary()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddScoped<IPaymobRuntimeApplicationService, PaymobRuntimeApplicationService>", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobRuntimeBridge, PaymobRuntimeBridge>", program, StringComparison.Ordinal);
    }
}
