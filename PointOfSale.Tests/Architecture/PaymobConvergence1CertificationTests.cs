namespace PointOfSale.Tests.Architecture;

public sealed class PaymobConvergence1CertificationTests
{
    [Fact]
    public void StorefrontAndRecoveryExecution_MustDelegateThroughPaymobRuntimeApplicationService()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var recovery = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutPaymentIntentRecoveryService.cs");

        Assert.Contains("IPaymobRuntimeApplicationService _paymobRuntimeApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("_paymobRuntimeApplicationService.CreatePaymentIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("PaymobRuntimePaymentIntentApplicationRequest", service, StringComparison.Ordinal);
        Assert.Contains("case \"PAYMOB\":", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("_paymobPaymentService.CreatePaymentIntentAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("case \"PAYMOB\":", recovery, StringComparison.Ordinal);
        Assert.Contains("_paymobPaymentService.CreatePaymentIntentAsync", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymobPaymentService_MustNotContainDirectProviderHttpExecutionOrRequestConstruction()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");

        Assert.DoesNotContain("IHttpClientFactory", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateClient(\"Paymob\")", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobAuthUrl", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobOrderUrl", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobPaymentKeyUrl", service, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthenticatePaymobAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymobOrderAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePaymentKeyAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderHttpExecution_MustRemainIsolatedToCertifiedPaymobAdapter()
    {
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var runtimeApplication = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");

        Assert.Contains("IPaymobOfficialApiClient", adapter, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentKeyAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", runtimeApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync(", runtimeApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookExecution_MustUseApplicationAdmissionBeforeLifecycleMutation()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs");

        var admissionIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var lifecycleIndex = service.IndexOf("ProcessPaymobTransactionAsync(", admissionIndex, StringComparison.Ordinal);
        var replayIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.PersistReplayRecordAsync", lifecycleIndex, StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(lifecycleIndex > admissionIndex);
        Assert.True(replayIndex > lifecycleIndex);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlePaymentResultAsync", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllersDtosRuntimeBridgeAndAdapters_MustRemainBehindTheirCertifiedBoundaries()
    {
        var storefrontController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/StorefrontPaymobPaymentsController.cs");
        var webhookController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymobWebhooksController.cs");
        var dtoContract = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/IPaymobPaymentService.cs");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");

        Assert.Contains("ICommerceHubPaymentOrchestrationService", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", storefrontController, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobRuntimeApplicationService", storefrontController, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPaymentOrchestrationService", webhookController, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobPaymentService", webhookController, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobRuntimeApplicationService", webhookController, StringComparison.Ordinal);
        Assert.Contains("PaymobPaymentIntentResponseDto", dtoContract, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", runtimeDirectory, StringComparison.Ordinal);
    }

    private static string ReadRepoFiles(string relativeDirectory)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
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
