namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure1Tests
{
    [Fact]
    public void OperatorWorkflow_MustConvergeThroughReviewApplicationService()
    {
        var adminController = ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs");
        var lifecycleController = ReadRepoFile("PointOfSale.API/Controllers/PaymentLifecycleController.cs");
        var application = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs");

        Assert.Contains("IManualPaymentReviewApplicationService", adminController, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.ApproveAsync", adminController, StringComparison.Ordinal);
        Assert.Contains("_manualPaymentReviewApplicationService.RejectAsync", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("_paymentLifecycleService.HandlePaymentResultAsync", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewApproval", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewRejection", adminController, StringComparison.Ordinal);

        Assert.DoesNotContain("[HttpPost(\"{orderId:guid}/wallet-approve\")]", lifecycleController, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost(\"{orderId:guid}/wallet-reject\")]", lifecycleController, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionCode = \"wallet-approve\"", lifecycleController, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionCode = \"wallet-reject\"", lifecycleController, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewApproval", lifecycleController, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewRejection", lifecycleController, StringComparison.Ordinal);

        Assert.Contains("public sealed class ManualPaymentReviewApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("_capabilityAdmissionAuthority.AdmitAsync", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.OperatorApproval", application, StringComparison.Ordinal);
        Assert.Contains("ManualPaymentOperationalCapability.OperatorRejection", application, StringComparison.Ordinal);
        Assert.Contains("_runtimeBridge.ExecuteEvidenceAsync", application, StringComparison.Ordinal);
        Assert.Contains("IsExpectedReviewAdmission", application, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", application, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWalletLifecycleWrites_MustConvergeThroughPaymentLifecycleService()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");
        var lifecycle = ReadRepoFile("PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs");
        var lifecycleInterface = ReadRepoFile("PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs");

        Assert.Contains("CreateReviewablePaymentIntentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("AdmitPaymentReviewAsync", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualWalletStateMachine.EnsureValidTransition", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewApproval", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleSource.PaymentReviewRejection", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntent.Status = PaymentIntentStatus.AwaitingVerification", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntent.ReferenceCode = result.InstructionIdentifier", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntent.ExpiresAtUtc = result.ExpiresAtUtc.UtcDateTime", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentIntent.ProviderIntentId = result.InstructionIdentifier", paymentService, StringComparison.Ordinal);

        Assert.Contains("Task<PaymentIntent> CreateReviewablePaymentIntentAsync", lifecycleInterface, StringComparison.Ordinal);
        Assert.Contains("Task<PaymentTransitionResult> AdmitPaymentReviewAsync", lifecycleInterface, StringComparison.Ordinal);
        Assert.Contains("public Task<PaymentIntent> CreateReviewablePaymentIntentAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_db.PaymentIntents.Add(paymentIntent)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("public async Task<PaymentTransitionResult> AdmitPaymentReviewAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("paymentIntent.Status = PaymentIntentStatus.AwaitingVerification", lifecycle, StringComparison.Ordinal);
        Assert.Contains("order.PaymentStatus = PaymentStatus.AwaitingVerification", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosureScope_MustNotIntroduceForbiddenWork()
    {
        var changedManualSources = string.Concat(
            ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs"),
            ReadRepoFile("PointOfSale.API/Controllers/AdminManualPaymentController.cs"),
            ReadRepoFile("PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs"));

        Assert.DoesNotContain("MigrationBuilder", changedManualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", changedManualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("FromSqlRaw", changedManualSources, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Callback", ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeApplicationService.cs"), StringComparison.Ordinal);
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
