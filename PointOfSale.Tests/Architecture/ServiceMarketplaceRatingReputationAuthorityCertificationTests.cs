namespace PointOfSale.Tests.Architecture;

public sealed class ServiceMarketplaceRatingReputationAuthorityCertificationTests
{
    [Fact]
    public void RatingRuntime_MustDeriveReviewIdentityReviewerAndValuesFromCompletedSqlRequestAuthority()
    {
        var reviewService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceReviewService.cs");
        var reviewEntity = ReadRepoFile("PointOfSale.Core/Entities/ServiceReview.cs");
        var configuration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/ServiceReviewConfiguration.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260611203933_ServiceMarketplaceRatingAuthorityHardening.cs");

        Assert.Contains("r.TenantId == tenantId", reviewService, StringComparison.Ordinal);
        Assert.Contains("request.BuyerUserId != buyerUserId", reviewService, StringComparison.Ordinal);
        Assert.Contains("request.ProviderUserId == buyerUserId", reviewService, StringComparison.Ordinal);
        Assert.Contains("request.Status != ServiceRequestStatus.CompletedByBuyer", reviewService, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(r => r.ServiceRequestId == dto.ServiceRequestId && !r.IsDeleted", reviewService, StringComparison.Ordinal);
        Assert.Contains("ServiceReview.Create(", reviewService, StringComparison.Ordinal);
        Assert.Contains("request.ServiceId", reviewService, StringComparison.Ordinal);
        Assert.Contains("request.ProviderUserId", reviewService, StringComparison.Ordinal);

        Assert.Contains("Id = Guid.NewGuid()", reviewEntity, StringComparison.Ordinal);
        Assert.Contains("Rating must be between 1 and 5", reviewEntity, StringComparison.Ordinal);
        Assert.Contains("UQ_ServiceReviews_ServiceRequest", configuration, StringComparison.Ordinal);
        Assert.Contains("CK_ServiceReviews_Rating_Range", configuration, StringComparison.Ordinal);
        Assert.Contains("CK_ServiceReviews_Rating_Range", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void RatingRuntime_MustKeepReviewContentImmutableAndReadsBoundToSqlReviewTruth()
    {
        var reviewService = ReadRepoFile("PointOfSale.Infrastructure/Services/ServiceReviewService.cs");
        var interceptor = ReadRepoFile("PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");

        Assert.Contains("ServiceReview rows are immutable after creation", interceptor, StringComparison.Ordinal);
        Assert.Contains("MarketingReviewEvidence rows are immutable after creation", interceptor, StringComparison.Ordinal);
        Assert.Contains("r.ServiceRequest.Status == ServiceRequestStatus.CompletedByBuyer", reviewService, StringComparison.Ordinal);
        Assert.Contains("r.ReviewerUserId == r.ServiceRequest.BuyerUserId", reviewService, StringComparison.Ordinal);
        Assert.Contains("r.ProviderUserId == r.ServiceRequest.ProviderUserId", reviewService, StringComparison.Ordinal);
        Assert.Contains("_catalogService.GetServiceByIdAsync(serviceId)", controller, StringComparison.Ordinal);
        Assert.Contains("_reviewService.GetReviewsByServiceIdAsync(serviceId)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(r => r.ServiceRequest!.ServiceId == serviceId && !r.IsDeleted)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void ReputationRuntime_MustRecomposeTrustFromSqlReviewsNotAggregateProjectionOrSearchDocuments()
    {
        var trustService = ReadRepoFile("PointOfSale.Infrastructure/Services/ProviderTrustService.cs");
        var searchService = ReadRepoFile("PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/MarketplaceController.cs");
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProviderTrustConsumer.cs");
        var driftJob = ReadRepoFile("PointOfSale.Infrastructure/Jobs/TrustDriftReconciliationJob.cs");

        Assert.Contains("Public trust authority is recomposed from SQL ServiceReview truth", trustService, StringComparison.Ordinal);
        Assert.Contains("ComposeSqlTrustAggregateAsync", trustService, StringComparison.Ordinal);
        Assert.Contains("row.Request.Status == ServiceRequestStatus.CompletedByBuyer", trustService, StringComparison.Ordinal);
        Assert.Contains("row.Review.ReviewerUserId == row.Request.BuyerUserId", trustService, StringComparison.Ordinal);
        Assert.Contains("ProviderTrustAggregate.FromSqlReviewTruth", trustService, StringComparison.Ordinal);
        Assert.DoesNotContain("Set<ProviderTrustAggregate>()", trustService, StringComparison.Ordinal);

        Assert.Contains("_providerTrustService.GetTrustAggregateAsync", searchService, StringComparison.Ordinal);
        Assert.Contains("AverageRating = trust?.AverageRating ?? 0", searchService, StringComparison.Ordinal);
        Assert.DoesNotContain("AverageRating = document.Trust?.AverageRating", searchService, StringComparison.Ordinal);
        Assert.Contains("_trustService.GetTrustAggregateAsync(service.ProfileId, cancellationToken)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ToDictionaryAsync(t => t.ProviderProfileId, t => t", controller, StringComparison.Ordinal);

        Assert.Contains("LoadSqlReviewAuthorityAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("Ignoring ServiceReviewCreatedEvent because current SQL review/request authority rejected it", consumer, StringComparison.Ordinal);
        Assert.Contains("row.Request.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer", consumer, StringComparison.Ordinal);
        Assert.Contains("row.Request.ProviderProfileId == aggregate.ProviderProfileId", driftJob, StringComparison.Ordinal);
        Assert.Contains("row.Request.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer", driftJob, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceMarketplaceRatingCertificationDocumentation_MustRecordS1EAuthorityFreeze()
    {
        var findings = ReadRepoFile("docs/architecture/service-marketplace-rating-reputation-authority-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "PASS S1-E Service Rating & Reputation Authority",
            "Review findings",
            "Reviewer findings",
            "Uniqueness findings",
            "Rating findings",
            "Content findings",
            "Reputation findings",
            "Visibility findings",
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

        Assert.Contains("PASS S1-E Service Rating & Reputation Authority", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS S1-E Service Rating & Reputation Authority Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS S1-E", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS S1-E Service Rating & Reputation Authority", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS S1-E Service Rating & Reputation Authority Freeze", codexRules, StringComparison.Ordinal);
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
