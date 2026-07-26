using System.Reflection;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPaymentOrchestrationArchitectureTests
{
    [Fact]
    public void Controller_MustDependOnCommerceHubPaymentOrchestrationForExecution()
    {
        var constructorParameterTypes = typeof(CommerceHubPaymentsController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Contains(typeof(ICommerceHubPaymentOrchestrationService), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ICommerceHubPaymentService), constructorParameterTypes);

        var controller = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPaymentsController.cs");
        Assert.Contains("_paymentOrchestrationService.ExecuteAsync", controller, StringComparison.Ordinal);
        Assert.Contains("_paymentOrchestrationService.ProcessWebhookAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IProviderPaymentGateway", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Orchestration_MustDependOnlyOnCanonicalPaymentService()
    {
        var constructorParameterTypes = typeof(CommerceHubPaymentOrchestrationService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Contains(typeof(IPaymentRuntime), constructorParameterTypes);
        Assert.DoesNotContain(typeof(ICommerceHubPaymentService), constructorParameterTypes);

        var orchestration = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs");
        Assert.Contains("IPaymentRuntime", orchestration, StringComparison.Ordinal);
        Assert.Contains("InitiateAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("ProcessWebhookAsync", orchestration, StringComparison.Ordinal);
        Assert.Contains("PaymentExecutionStarted", orchestration, StringComparison.Ordinal);
        Assert.Contains("PaymentExecutionSucceeded", orchestration, StringComparison.Ordinal);
        Assert.Contains("PaymentExecutionFailed", orchestration, StringComparison.Ordinal);
        Assert.Contains("ProviderSelected", orchestration, StringComparison.Ordinal);
        Assert.Contains("WebhookReceived", orchestration, StringComparison.Ordinal);
        Assert.Contains("ActivitySource", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommerceHubPaymentService", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("IProviderPaymentGateway", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", orchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", orchestration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", orchestration, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_MustRegisterPaymentOrchestrationAsScopedRuntimeBoundary()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains(
            "AddScoped<PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentOrchestrationService, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentOrchestrationService>",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddScoped<PointOfSale.API.Services.CommerceHub.IPaymentRuntime, PointOfSale.API.Services.CommerceHub.CommerceHubPaymentRuntime>",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<PointOfSale.API.Services.CommerceHub.ICommerceHubPaymentOrchestrationService",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderGateways_MustNotBeRegisteredAsCommerceHubOnlineRuntimeAuthority()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.DoesNotContain(
            "AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IPaymentGateway, PointOfSale.API.Services.Payments.ProviderContracts.PaymentGateway>",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddScoped<PointOfSale.API.Services.Payments.ProviderContracts.IProviderPaymentGateway",
            program,
            StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
