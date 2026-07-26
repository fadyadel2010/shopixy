namespace PointOfSale.Tests.Architecture;

public sealed class StripeConvergence1CertificationTests
{
    [Fact]
    public void StorefrontStripeExecutionAndRecovery_MustDelegateThroughCanonicalApplicationRuntimePath()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var recoveryWorker = CertificationSource.ReadRepoFile("PointOfSale.API/Services/BackgroundTasks/RedirectUrlRecoveryHostedService.cs");

        Assert.Contains("IStripeRuntimeApplicationService _stripeRuntimeApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("_stripeRuntimeApplicationService.CreatePaymentIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("RehydrateStripeProviderIntentAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IStripeRuntimeApplicationService>", recoveryWorker, StringComparison.Ordinal);
        Assert.Contains("stripeRuntimeApplicationService.CreatePaymentIntentAsync", recoveryWorker, StringComparison.Ordinal);

        Assert.DoesNotContain("new PaymentIntentService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentIntentService", recoveryWorker, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetAsync(", service, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetAsync(", recoveryWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookExecution_MustRunRuntimeAdmissionBeforeLifecyclePersistence()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs");

        var admissionIndex = service.IndexOf("_stripeWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var lifecycleIndex = service.IndexOf("_paymentLifecycleService.HandlePaymentResultAsync", admissionIndex, StringComparison.Ordinal);
        var refundIndex = service.IndexOf("_paymentLifecycleService.RecordRefundAsync", admissionIndex, StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(lifecycleIndex > admissionIndex);
        Assert.True(refundIndex > admissionIndex);
        Assert.Contains("StripeWebhookOutcomeAdmissionService.Evaluate", integration, StringComparison.Ordinal);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void StripeSdkExecutionPrimitives_MustStayInsideCertifiedAdaptersOrDocumentedLegacyExternalContexts()
    {
        var sdkCallSites = FindRepoFilesContaining(
            "new PaymentIntentService",
            "new RefundService",
            "new ChargeService",
            "EventUtility.ConstructEvent");

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripePaymentIntentAdapter.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripeAdvancedOperationsAdapter.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripeWebhookEvidenceAdapter.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripeFoundationServiceCollectionExtensions.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripeFoundationService.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs"),
            Normalize("PointOfSale.API/Services/Payments/Stripe/IStripePaymentService.cs"),
            Normalize("PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs"),
            Normalize("PointOfSale.API/Services/CommerceHub/CommerceHubRefundGatewayService.cs")
        };

        var unexpected = sdkCallSites
            .Where(path => !allowed.Contains(Normalize(path)))
            .ToArray();

        Assert.Empty(unexpected);
    }

    private static IReadOnlyCollection<string> FindRepoFilesContaining(params string[] needles)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return Directory.EnumerateFiles(Path.Combine(root, "PointOfSale.API"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return needles.Any(needle => source.Contains(needle, StringComparison.Ordinal));
            })
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/');

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
