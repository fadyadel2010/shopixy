namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRatingEligibilityAuthorityCertificationTests
{
    [Fact]
    public void RatingEligibility_MustBindFinalCompletionBuyerAndExistingReviewAuthority()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var eligibility = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingEligibility.ts");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var details = ReadRepoFile("PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts");

        Assert.Contains("existingReviewId = existingReviews.GetValueOrDefault(r.Id)", controller, StringComparison.Ordinal);
        Assert.Contains("canReview = isBuyer && request.CanBeReviewed() && !existingReviewId.HasValue", controller, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestRatingEligibility", eligibility, StringComparison.Ordinal);
        Assert.Contains("actorUserId !== buyerUserId", eligibility, StringComparison.Ordinal);
        Assert.Contains("buyerUserId === providerUserId", eligibility, StringComparison.Ordinal);
        Assert.Contains("lifecycle.statusKey === 'completedByBuyer'", eligibility, StringComparison.Ordinal);
        Assert.Contains("capabilities.canReview === (capabilities.existingReviewId !== null)", eligibility, StringComparison.Ordinal);
        Assert.Contains("ratingEligibility?.canRate", myRequests, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestRatingEligibility", details, StringComparison.Ordinal);
        Assert.DoesNotContain("{lifecycle.canReview ? (", myRequests, StringComparison.Ordinal);
    }

    [Fact]
    public void RatingWorkflow_MustUsePosEndpointAndFreshScopedBackendRevalidation()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var api = ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingWorkflow.ts");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var hook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts");

        Assert.Contains("[HttpPost(\"service-reviews\")]", controller, StringComparison.Ordinal);
        Assert.Contains("_reviewService.CreateReviewAsync", controller, StringComparison.Ordinal);
        Assert.Contains("apiClient.post('/marketplace/service-reviews'", api, StringComparison.Ordinal);
        Assert.DoesNotContain("apiClient.post('/marketplace/reviews'", api, StringComparison.Ordinal);
        Assert.Contains("executeServiceRequestRatingWorkflow", workflow, StringComparison.Ordinal);
        Assert.Contains("const preflightRequest = await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("const settledRequest = await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("authorityScope !== getCurrentAuthorityScope()", workflow, StringComparison.Ordinal);
        Assert.Contains("ratingExecutions", workflow, StringComparison.Ordinal);

        foreach (var source in new[] { myRequests, hook })
        {
            Assert.Contains("executeServiceRequestRatingWorkflow", source, StringComparison.Ordinal);
            Assert.Contains("retry: false", source, StringComparison.Ordinal);
            Assert.Contains("networkMode: 'online'", source, StringComparison.Ordinal);
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", source, StringComparison.Ordinal);
            Assert.DoesNotContain("onMutate", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RatingAuthority_MustRemainScopedAndWithheldDuringRefresh()
    {
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var requestDetails = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var realtime = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useChat.ts");

        Assert.Contains("if (isLoading || isFetching)", myRequests, StringComparison.Ordinal);
        Assert.Contains("refetchInterval: 15_000", requestDetails, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("AUTHORITY_REVALIDATION_INTERVAL_MS", boundary, StringComparison.Ordinal);
        Assert.Contains("marketplaceKeys.scope(authorityScope)", realtime, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", realtime, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs5A()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-rating-eligibility-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S5-A",
            "## Eligibility Findings",
            "## Buyer Findings",
            "## Provider Findings",
            "## Completion Findings",
            "## Visibility Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
            "## Failure Findings",
            "## Adversarial Findings",
            "## Runtime Hardening Performed",
            "## Exact Runtime Files Modified",
            "## Regression Freezes Added",
            "## Architecture Updates",
            "## Explicit Answers"
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS F-S5-A", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-A", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-A", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-A", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-A", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
