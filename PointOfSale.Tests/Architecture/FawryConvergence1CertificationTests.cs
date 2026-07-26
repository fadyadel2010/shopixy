namespace PointOfSale.Tests.Architecture;

public sealed class FawryConvergence1CertificationTests
{
    [Fact]
    public void ApplicationExecution_MustDelegateFawryThroughRuntimeApplicationServiceBeforeGenericAdapters()
    {
        var paymentService = ReadRepoFile("PointOfSale.API/Services/Payments/PaymentService.cs");

        Assert.Contains("IFawryRuntimeApplicationService", paymentService, StringComparison.Ordinal);
        Assert.Contains("InitiateFawryPersistedPaymentIntentAsync(paymentIntent", paymentService, StringComparison.Ordinal);
        Assert.Contains("new FawryRuntimePaymentIntentApplicationRequest", paymentService, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentIntentAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("BeginChargeRegistrationAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("PersistAcceptedReferenceAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("ApplyAcceptedReference", paymentService, StringComparison.Ordinal);

        var fawryBranch = paymentService.IndexOf("provider == PaymentProvider.Fawry", StringComparison.Ordinal);
        var persistedMethodStart = paymentService.IndexOf("private async Task InitiatePersistedPaymentIntentAsync", StringComparison.Ordinal);
        var genericAdapterLookup = paymentService.IndexOf("_paymentAdapters.FirstOrDefault", persistedMethodStart, StringComparison.Ordinal);
        Assert.True(fawryBranch >= 0, "Fawry must have an explicit application-service branch.");
        Assert.True(genericAdapterLookup > fawryBranch, "Fawry must converge before the legacy generic adapter execution path.");
        Assert.DoesNotContain("FawryPaymentAdapter", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsJsonAsync", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookExecution_MustDelegateFawryThroughRuntimeIntegrationBeforeLifecyclePersistence()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");

        Assert.Contains("IFawryWebhookRuntimeIntegrationService", controller, StringComparison.Ordinal);
        Assert.Contains("EvaluateAndExecuteAsync", controller, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookRuntimeAdmissionRequest", controller, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed", controller, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission.CreateRequest", controller, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", controller, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifySignature(", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryWebhookDto", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectFawryHttpExecution_MustRemainLimitedToCertifiedProviderAdapters()
    {
        var violations = Directory
            .EnumerateFiles(RepoPath("PointOfSale.API"), "*.cs", SearchOption.AllDirectories)
            .Where(path => RelativePath(path).Contains("/Fawry/", StringComparison.Ordinal)
                || Path.GetFileName(path).Contains("Fawry", StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("PostAsJsonAsync", StringComparison.Ordinal)
                    || source.Contains("GetAsync", StringComparison.Ordinal);
            })
            .Select(RelativePath)
            .Except(new[]
            {
                "PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs",
                "PointOfSale.API/Services/Payments/Fawry/FawryAdvancedOperationsAdapter.cs"
            }, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Runtime_MustRemainFawryFreeAndFrozen()
    {
        var runtimeSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(RepoPath("PointOfSale.Core/Payments/Runtime"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Fawry", runtimeSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TenantResolver_MustResolveOfficialFawryWebhookPayloadFields()
    {
        var resolver = ReadRepoFile("PointOfSale.API/Services/Payments/IPaymentWebhookTenantResolver.cs");

        Assert.Contains("GetString(root, \"fawryRefNumber\", \"referenceNumber\")", resolver, StringComparison.Ordinal);
        Assert.Contains("GetString(root, \"merchantRefNumber\", \"merchantRefNum\")", resolver, StringComparison.Ordinal);
        Assert.Contains("ResolveByReferenceCodeAsync(referenceCode", resolver, StringComparison.Ordinal);
        Assert.Contains("ResolveByPaymentReferenceAsync(merchantReference", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationArtifact_MustRecordFawryConvergenceDecision()
    {
        var artifact = ReadRepoFile("docs/architecture/FAWRY_CONVERGENCE_1.md");

        Assert.Contains("FAWRY-CONVERGENCE-1", artifact, StringComparison.Ordinal);
        Assert.Contains("| Application execution converged? | Yes.", artifact, StringComparison.Ordinal);
        Assert.Contains("| Webhook execution converged? | Yes.", artifact, StringComparison.Ordinal);
        Assert.Contains("| Legacy execution removed? | Yes", artifact, StringComparison.Ordinal);
        Assert.Contains("| Runtime preserved? | Yes.", artifact, StringComparison.Ordinal);
        Assert.Contains("| Convergence certified? | Yes", artifact, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static string RepoPath(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string RelativePath(string fullPath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
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
