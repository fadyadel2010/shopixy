using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobCertificationClosure1Tests
{
    [Fact]
    public void RuntimeRejection_MustBlockLifecycleMutation()
    {
        var runtimeAdmission = CreateAdmission(
            adapterSucceeded: false,
            runtimeStatus: RuntimeOverallStatus.Failed);
        var lifecycleMutated = false;

        var exception = Assert.Throws<PaymentException>(() =>
        {
            PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome(runtimeAdmission);
            lifecycleMutated = true;
        });

        Assert.Equal(PaymobRuntimeOutcomeGuard.RejectionCode, exception.ErrorCode);
        Assert.False(lifecycleMutated);
    }

    [Fact]
    public void RuntimeFailure_MustBlockLifecycleMutation()
    {
        var runtimeAdmission = CreateAdmission(
            adapterSucceeded: true,
            runtimeStatus: RuntimeOverallStatus.Failed);
        var lifecycleMutated = false;

        Assert.Throws<PaymentException>(() =>
        {
            PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome(runtimeAdmission);
            lifecycleMutated = true;
        });

        Assert.False(runtimeAdmission.AdmittedToRuntime);
        Assert.False(lifecycleMutated);
    }

    [Fact]
    public void RuntimeSuccess_MustAllowLifecycleMutation()
    {
        var runtimeAdmission = CreateAdmission(
            adapterSucceeded: true,
            runtimeStatus: RuntimeOverallStatus.Succeeded);
        var lifecycleMutated = false;

        PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome(runtimeAdmission);
        lifecycleMutated = true;

        Assert.True(runtimeAdmission.AdmittedToRuntime);
        Assert.True(lifecycleMutated);
    }

    [Fact]
    public void ApplicationBoundary_MustEnforceRuntimeOutcomeBeforeSqlAndLifecycleMutation()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var guard = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeOutcomeGuard.cs");

        var admissionIndex = service.IndexOf(
            "_paymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync",
            StringComparison.Ordinal);
        var guardIndex = service.IndexOf(
            "PaymobRuntimeOutcomeGuard.EnsureRequiredOutcome(runtimeAdmission);",
            admissionIndex,
            StringComparison.Ordinal);
        var transactionIndex = service.IndexOf(
            "_dbContext.Database.BeginTransactionAsync",
            admissionIndex,
            StringComparison.Ordinal);
        var lifecycleIndex = service.IndexOf(
            "ProcessPaymobTransactionAsync(",
            admissionIndex,
            StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(guardIndex > admissionIndex);
        Assert.True(transactionIndex > guardIndex);
        Assert.True(lifecycleIndex > transactionIndex);
        Assert.Contains(
            "runtimeAdmission.Admission.AdmittedToRuntime &&",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("!runtimeAdmission.AdmittedToRuntime", guard, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_RUNTIME_OUTCOME_REJECTED", guard, StringComparison.Ordinal);
        Assert.Contains("throw new PaymentException", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuard_MustRemainOutsideFrozenRuntimeBridgeAndAdapters()
    {
        var runtime = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var bridge = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var providerAdapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var webhookAdapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs");

        Assert.DoesNotContain("PaymobRuntimeOutcomeGuard", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobRuntimeOutcomeGuard", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobRuntimeOutcomeGuard", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobRuntimeOutcomeGuard", webhookAdapter, StringComparison.Ordinal);
    }

    private static PaymobWebhookRuntimeAdmissionResult CreateAdmission(
        bool adapterSucceeded,
        RuntimeOverallStatus runtimeStatus)
    {
        var adapterResult = new PaymobWebhookEvidenceAdapterResult
        {
            Succeeded = adapterSucceeded,
            RuntimeResult = new RuntimeResult { OverallStatus = runtimeStatus },
            PaymobTransactionId = "paymob-certification-closure-1",
            PaymobEventType = "TRANSACTION"
        };

        return PaymobWebhookRuntimeAdmissionResult.Admitted(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new PaymobWebhookOutcomeAdmission(
                PaymobWebhookOutcomeClassification.Terminal,
                true,
                "TERMINAL"),
            new PaymobWebhookReplayContext(),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            adapterResult);
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
