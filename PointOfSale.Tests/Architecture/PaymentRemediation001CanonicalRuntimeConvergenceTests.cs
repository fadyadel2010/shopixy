using System.Reflection;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Payments.Runtime.Domain;
using CorePaymentRuntime = PointOfSale.Core.Payments.Runtime.Contracts.IPaymentRuntime;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentRemediation001CanonicalRuntimeConvergenceTests
{
    private static readonly string[] PaymentControllerPaths =
    [
        "PointOfSale.API/Controllers/StorefrontStripePaymentsController.cs",
        "PointOfSale.API/Controllers/StripeWebhooksController.cs",
        "PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs",
        "PointOfSale.API/Controllers/PaymobWebhooksController.cs"
    ];

    [Fact]
    public void PaymentControllers_MustDelegateOnlyToCommerceHubPaymentOrchestration()
    {
        foreach (var path in PaymentControllerPaths)
        {
            var source = CertificationSource.ReadRepoFile(path);

            Assert.Contains("ICommerceHubPaymentOrchestrationService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IPaymobPaymentService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IStripePaymentService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IPaymobRuntimeApplicationService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IStripeRuntimeApplicationService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EventUtility.ConstructEvent", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentIntentService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IPaymobOfficialApiClient", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PaymentControllerConstructors_MustNotAcceptProviderRuntimeServices()
    {
        var controllerTypes = new[]
        {
            typeof(StorefrontStripePaymentsController),
            typeof(StripeWebhooksController),
            typeof(StorefrontPaymobPaymentsController),
            typeof(PaymobWebhooksController)
        };

        foreach (var controllerType in controllerTypes)
        {
            var constructor = Assert.Single(controllerType.GetConstructors());
            var parameterTypes = constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.Contains(typeof(ICommerceHubPaymentOrchestrationService), parameterTypes);
            Assert.DoesNotContain(typeof(IStripePaymentService), parameterTypes);
            Assert.DoesNotContain(typeof(IPaymobPaymentService), parameterTypes);
        }
    }

    [Fact]
    public void CommerceHubPaymentRuntime_MustUseProviderAdaptersForOnlineProviders()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");

        Assert.Contains("IEnumerable<IPaymentProviderAdapter> providerAdapters", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Stripe).CreateSessionAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Stripe).ParseWebhookAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Paymob).CreateSessionAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetProviderAdapter(CommerceHubPaymentMethods.Paymob).ParseWebhookAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventUtility.ConstructEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentIntentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StripeClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StripeAndPaymobProviderProtocol_MustLiveInsideProviderAdapters()
    {
        var stripeAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeAdapter.cs");
        var paymobAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobAdapter.cs");
        var stripeRegistration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs");
        var paymobRegistration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");

        Assert.True(typeof(IPaymentProviderAdapter).IsAssignableFrom(typeof(StripeAdapter)));
        Assert.True(typeof(IPaymentProviderAdapter).IsAssignableFrom(typeof(PaymobAdapter)));
        Assert.Contains("class StripeAdapter : IPaymentProviderAdapter", stripeAdapter, StringComparison.Ordinal);
        Assert.Contains("EventUtility.ConstructEvent", stripeAdapter, StringComparison.Ordinal);
        Assert.Contains("new PaymentIntentService", stripeAdapter, StringComparison.Ordinal);
        Assert.Contains("class PaymobAdapter : IPaymentProviderAdapter", paymobAdapter, StringComparison.Ordinal);
        Assert.Contains("IPaymobOfficialApiClient", paymobAdapter, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymentProviderAdapter, StripeAdapter>", stripeRegistration, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymentProviderAdapter, PaymobAdapter>", paymobRegistration, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentRuntimeRegistration_MustExposeExactlyOneActiveRuntimeAuthority()
    {
        var extension = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Payments/Runtime/PaymentRuntimeServiceCollectionExtensions.cs");
        var placeholdersPath = Path.Combine(FindRepoRoot(), "PointOfSale.Infrastructure", "Payments", "Runtime", "PaymentRuntimePlaceholders.cs");

        Assert.True(typeof(CorePaymentRuntime).IsAssignableFrom(typeof(PaymentRuntimeOrchestrator)));
        Assert.Contains("TryAddSingleton<PaymentRuntimeOrchestrator>", extension, StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton<IPaymentRuntime>", extension, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<PaymentRuntimeOrchestrator>", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentRuntime", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentProvider", extension, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledPaymentPolicyEngine", extension, StringComparison.Ordinal);
        Assert.False(File.Exists(placeholdersPath));
    }

    [Fact]
    public void LegacyProviderRuntimeServices_MustNotBeAuthoritativeForCommerceHubOnlinePayments()
    {
        var checkoutRecovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");
        var commerceHubOrchestration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentOrchestrationService.cs");
        var commerceHubRuntime = CertificationSource.ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs");

        Assert.DoesNotContain("IPaymobPaymentService", commerceHubOrchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", commerceHubOrchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", commerceHubRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", commerceHubRuntime, StringComparison.Ordinal);
        Assert.Contains("IPaymentRuntime", commerceHubOrchestration, StringComparison.Ordinal);
        Assert.Contains("IEnumerable<IPaymentProviderAdapter> providerAdapters", commerceHubRuntime, StringComparison.Ordinal);

        Assert.Contains("IPaymentRuntime", checkoutRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", checkoutRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("IStripePaymentService", checkoutRecovery, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
