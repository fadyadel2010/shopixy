namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRatingStateAuthorityCertificationTests
{
    [Fact]
    public void RatingState_MustBeBuyerBoundFinalAndNonContradictory()
    {
        var state = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingStateAuthority.ts");
        var eligibility = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingEligibility.ts");

        Assert.Contains("lifecycle.statusKey !== 'completedByBuyer'", state, StringComparison.Ordinal);
        Assert.Contains("actorUserId !== buyerUserId", state, StringComparison.Ordinal);
        Assert.Contains("buyerUserId === providerUserId", state, StringComparison.Ordinal);
        Assert.Contains("typeof authority.canReview !== 'boolean'", state, StringComparison.Ordinal);
        Assert.Contains("state: 'unrated'", state, StringComparison.Ordinal);
        Assert.Contains("state: 'rated'", state, StringComparison.Ordinal);
        Assert.Contains("reviewId === null", state, StringComparison.Ordinal);
        Assert.Contains("deriveServiceRequestRatingStatePresentation", eligibility, StringComparison.Ordinal);
    }

    [Fact]
    public void RatingState_SurfacesMustConsumeOneSharedBoundary()
    {
        var card = ReadRepoFile("PointOfSale.Web/src/marketplace/components/RequestCard.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var incoming = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx");
        var chat = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/ChatPage.tsx");
        var details = ReadRepoFile("PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts");
        var badge = ReadRepoFile("PointOfSale.Web/src/marketplace/components/RatingStateBadge.tsx");

        foreach (var source in new[] { card, myRequests, incoming, chat, details })
        {
            Assert.Contains("deriveServiceRequestRatingStatePresentation", source, StringComparison.Ordinal);
        }

        Assert.Contains("RatingStateBadge", card, StringComparison.Ordinal);
        Assert.Contains("RatingStateBadge", myRequests, StringComparison.Ordinal);
        Assert.Contains("RatingStateBadge", chat, StringComparison.Ordinal);
        Assert.Contains("ratingEligibility?.canRate", myRequests, StringComparison.Ordinal);
        Assert.DoesNotContain("data-review-id", badge, StringComparison.Ordinal);
        Assert.DoesNotContain("navigate(ratingState.reviewId", myRequests, StringComparison.Ordinal);
    }

    [Fact]
    public void RatingState_CacheRealtimeActorAndHistoryMustRemainDerivative()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var requestDetails = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts");
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingWorkflow.ts");

        Assert.Contains("event.authorityScope === authorityScope", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("if (isLoading || isFetching)", myRequests, StringComparison.Ordinal);
        Assert.Contains("refetchInterval: 15_000", requestDetails, StringComparison.Ordinal);
        Assert.Contains("authorityScope !== getCurrentAuthorityScope()", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("setQueryData", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs5C()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-rating-state-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S5-C",
            "## Review-Identity Findings",
            "## Rated/Unrated Findings",
            "## Visibility Findings",
            "## Action-Consistency Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
            "## Failure Findings",
            "## Concurrency Findings",
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

        Assert.Contains("PASS F-S5-C", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-C", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-C", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-C", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-C", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
