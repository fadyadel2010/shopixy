namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceCompletionAuthorityCertificationTests
{
    [Fact]
    public void CompletionRuntime_MustDeriveProviderBuyerAndFinalClosureFromSqlRequestAuthority()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/ServiceRequest.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("LoadRequestForMutationAsync", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("r.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("request.ProviderUserId != providerUserId", service, StringComparison.Ordinal);
        Assert.Contains("request.BuyerUserId != buyerUserId", service, StringComparison.Ordinal);
        Assert.Contains("request.MarkCompletedByProvider();", service, StringComparison.Ordinal);
        Assert.Contains("request.ConfirmCompletedByBuyer();", service, StringComparison.Ordinal);

        Assert.Contains("Status != ServiceRequestStatus.Accepted", entity, StringComparison.Ordinal);
        Assert.Contains("Status = ServiceRequestStatus.CompletedByProvider", entity, StringComparison.Ordinal);
        Assert.Contains("Status != ServiceRequestStatus.CompletedByProvider", entity, StringComparison.Ordinal);
        Assert.Contains("Status = ServiceRequestStatus.CompletedByBuyer", entity, StringComparison.Ordinal);
        Assert.Contains("Status == ServiceRequestStatus.CompletedByBuyer", entity, StringComparison.Ordinal);

        Assert.Contains("ValidateStatusTransition", interceptor, StringComparison.Ordinal);
        Assert.Contains("(ServiceRequestStatus.Accepted, ServiceRequestStatus.CompletedByProvider) => true", interceptor, StringComparison.Ordinal);
        Assert.Contains("(ServiceRequestStatus.CompletedByProvider, ServiceRequestStatus.CompletedByBuyer) => true", interceptor, StringComparison.Ordinal);
        Assert.Contains("ValidateLifecycleTimestampMutation", interceptor, StringComparison.Ordinal);
        Assert.Contains("can only change with its SQL lifecycle transition", interceptor, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionEvidenceChatAndRating_MustNotTreatProviderOnlyCompletionAsFinalAuthority()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceRequestService.cs");
        var entity = ReadRepoFile("PointOfSale.Core/Entities/ServiceRequest.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var reviewService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceReviewService.cs");
        var chat = ReadRepoFile("PointOfSale.Infrastructure/Services/ChatService.cs");

        Assert.Contains("SendSystemMessageAsync(requestId, \"Provider marked service as completed\"", service, StringComparison.Ordinal);
        Assert.Contains("SendSystemMessageAsync(requestId, \"Buyer confirmed service completion\"", service, StringComparison.Ordinal);
        Assert.True(
            service.IndexOf("SendSystemMessageAsync(requestId, \"Buyer confirmed service completion\"", StringComparison.Ordinal) <
            service.IndexOf("transaction.CommitAsync", service.IndexOf("ConfirmCompletedByBuyerAsync", StringComparison.Ordinal), StringComparison.Ordinal));

        Assert.Contains("return Status == ServiceRequestStatus.CompletedByBuyer", entity, StringComparison.Ordinal);
        Assert.Contains("request.Status != ServiceRequestStatus.CompletedByBuyer", reviewService, StringComparison.Ordinal);
        Assert.Contains("r.TenantId == tenantId", reviewService, StringComparison.Ordinal);
        Assert.Contains("r.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("CompletedByBuyer ||", controller, StringComparison.Ordinal);
        Assert.Contains("Status == ServiceRequestStatus.Pending", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceCompletionCertificationDocumentation_MustRecordS1DAuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-completion-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS S1-D Service Marketplace Completion Authority",
            "Completion findings",
            "Provider findings",
            "Buyer findings",
            "Mutual completion findings",
            "Lifecycle findings",
            "Evidence findings",
            "Chat-boundary findings",
            "Rating-eligibility findings",
            "Projection findings",
            "Isolation findings",
            "Concurrency findings",
            "Historical findings",
            "Adversarial findings",
            "Runtime Hardening Performed",
            "Exact Runtime Files Modified",
            "Regression Freezes Added",
            "Architecture Updates"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS S1-D Service Marketplace Completion Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS S1-D Service Marketplace Completion Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS S1-D", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS S1-D Service Marketplace Completion Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS S1-D Service Marketplace Completion Authority Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
