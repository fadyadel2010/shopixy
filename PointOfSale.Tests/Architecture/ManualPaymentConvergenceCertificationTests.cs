namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentConvergenceCertificationTests
{
    [Fact]
    public void Initialization_MustConvergeThroughCertifiedApplicationPath()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("IManualPaymentRuntimeApplicationService", paymentService, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentRuntimeApplicationService.InitializeReferencePaymentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentRuntimeApplicationRequest", paymentService, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteAsync", application, StringComparison.Ordinal);
        Assert.Contains("_adapter.CreatePendingPayment", bridge, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWalletPaymentAdapter", program, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceAdmission_MustConvergeThroughCertifiedApplicationPath()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");

        Assert.Contains("IManualPaymentEvidenceApplicationService", paymentService, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentEvidenceApplicationService.AdmitEvidenceAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentEvidenceApplicationRequest", paymentService, StringComparison.Ordinal);
        Assert.Contains("_evidenceAdapter.Translate", application, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteEvidenceAsync", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentEvidenceExecutionRequest", bridge, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyPath_MustBeRemovedAndRuntimeBridgeEnforced()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var controllers = string.Concat(
            ReadRepoFile("PointOfSale.API/Controllers/StorefrontManualWalletController.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"));

        Assert.DoesNotContain("ManualWalletPaymentAdapter", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new ManualWalletPaymentAdapter", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePendingPayment", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPaymentProviderAdapter", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("IManualPaymentProviderAdapter", controllers, StringComparison.Ordinal);
    }

    [Fact]
    public void CertifiedLayers_MustNotAcquireForbiddenResponsibilities()
    {
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");

        Assert.Contains("IManualPaymentReviewApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("IPaymentLifecycleService", application, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", evidenceAdapter, StringComparison.Ordinal);
    }

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
}
