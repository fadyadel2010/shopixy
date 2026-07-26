namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceFrontendRatingSubmissionWorkflowAuthorityCertificationTests
{
    [Fact]
    public void RatingSubmission_MustUseFreshBackendBoundPayloadAndAuthoritativeSuccessProof()
    {
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingWorkflow.ts");
        var api = ReadRepoFile("PointOfSale.Web/src/marketplace/api/marketplaceApi.ts");

        Assert.Contains("deriveServiceReviewSubmissionAuthority", workflow, StringComparison.Ordinal);
        Assert.Contains("requestId !== authoritativeRequestId", workflow, StringComparison.Ordinal);
        Assert.Contains("isMarketing !== (review.evidence !== undefined)", workflow, StringComparison.Ordinal);
        Assert.Contains("const preflightRequest = await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("mutationResponse = await mutate(submissionAuthority)", workflow, StringComparison.Ordinal);
        Assert.Contains("deriveServiceReviewCreationAuthority", workflow, StringComparison.Ordinal);
        Assert.Contains("const settledRequest = await revalidate()", workflow, StringComparison.Ordinal);
        Assert.Contains("settledEligibility.existingReviewId", workflow, StringComparison.Ordinal);
        Assert.Contains("apiClient.post('/marketplace/service-reviews'", api, StringComparison.Ordinal);
    }

    [Fact]
    public void RatingMutationAndDialog_MustFenceDuplicatesAndPreserveFailureAsFailure()
    {
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingWorkflow.ts");
        var modal = ReadRepoFile("PointOfSale.Web/src/marketplace/components/ReviewModal.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");
        var hook = ReadRepoFile("PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts");

        Assert.Contains("ratingExecutions.has(executionKey)", workflow, StringComparison.Ordinal);
        Assert.Contains("await revalidate().catch(() => null)", workflow, StringComparison.Ordinal);
        Assert.Contains("submitPendingRef.current", modal, StringComparison.Ordinal);
        Assert.Contains("await onSubmit(review)", modal, StringComparison.Ordinal);
        Assert.Contains("onClose()", modal, StringComparison.Ordinal);
        Assert.Contains("key={reviewingRequest.requestId}", myRequests, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to submit review:", myRequests, StringComparison.Ordinal);

        foreach (var source in new[] { myRequests, hook })
        {
            Assert.Contains("retry: false", source, StringComparison.Ordinal);
            Assert.Contains("networkMode: 'online'", source, StringComparison.Ordinal);
            Assert.Contains("publishMarketplaceAuthorityInvalidation(authorityScope)", source, StringComparison.Ordinal);
            Assert.Contains("marketplaceKeys.scope(authorityScope)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("setQueryData", source, StringComparison.Ordinal);
            Assert.DoesNotContain("onMutate", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RatingWorkflow_MustRemainActorTenantCacheRealtimeAndHistoryDerivative()
    {
        var workflow = ReadRepoFile("PointOfSale.Web/src/marketplace/ratingWorkflow.ts");
        var authority = ReadRepoFile("PointOfSale.Web/src/marketplace/authority.ts");
        var boundary = ReadRepoFile("PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx");
        var myRequests = ReadRepoFile("PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx");

        Assert.Contains("authorityScope !== getCurrentAuthorityScope()", workflow, StringComparison.Ordinal);
        Assert.Contains("marketplaceAuthorityChannelName", authority, StringComparison.Ordinal);
        Assert.Contains("event.authorityScope === authorityScope", authority, StringComparison.Ordinal);
        Assert.Contains("queryClient.removeQueries", boundary, StringComparison.Ordinal);
        Assert.Contains("isMarketplaceAuthorityInvalidation", boundary, StringComparison.Ordinal);
        Assert.Contains("if (isLoading || isFetching)", myRequests, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("history.", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocumentation_MustRecordPassFs5B()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-frontend-rating-submission-workflow-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS F-S5-B",
            "## Workflow Findings",
            "## Submission Findings",
            "## Mutation Findings",
            "## Success Findings",
            "## Failure Findings",
            "## Cache Findings",
            "## Realtime Findings",
            "## Actor-Transition Findings",
            "## History Findings",
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

        Assert.Contains("PASS F-S5-B", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-B", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-B", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-B", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS F-S5-B", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
