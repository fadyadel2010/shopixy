namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubReviewsRatingsAuthorityCertificationTests
{
    [Fact]
    public void PublicReviewReadsAndRatingSummaries_MustDeriveFromSqlApprovedReviewTruth()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/MarketplaceReviews/MarketplaceProductReviewService.cs");

        Assert.Contains("BuildSqlRatingSummaryAsync", service, StringComparison.Ordinal);
        Assert.Contains("_dbContext.ProductReviews", service, StringComparison.Ordinal);
        Assert.Contains("review.Status == ProductReviewStatus.Approved", service, StringComparison.Ordinal);
        Assert.Contains("review.ProductId == productId", service, StringComparison.Ordinal);
        Assert.Contains("review.IsDeleted", service, StringComparison.Ordinal);
        Assert.Contains("IsMarketplaceProductPublishedAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("_mongoDbContext.MarketplaceProductReviews", service, StringComparison.Ordinal);
        Assert.DoesNotContain("_mongoDbContext.ProductReviewSummaries", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IRedisJsonCacheService", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewEligibilityAndIdentity_MustRequireSqlCustomerOrderOwnershipAndUniqueness()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/MarketplaceReviews/MarketplaceProductReviewService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("HasVerifiedPurchaseAsync(customerId, request.ProductId", service, StringComparison.Ordinal);
        Assert.Contains("order.CustomerId == customerId", service, StringComparison.Ordinal);
        Assert.Contains("item.ProductId == productId", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderItemStatus.Delivered", service, StringComparison.Ordinal);
        Assert.Contains("REVIEW_ALREADY_EXISTS", service, StringComparison.Ordinal);
        Assert.Contains("HasIndex(review => new { review.ProductId, review.UserId })", dbContext, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasFilter(\"\\\"IsDeleted\\\" = false\")", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ModerationPublicationAndProjectionReplay_MustReconcileFromCurrentSqlReviewState()
    {
        var backbone = ReadRepoFile("PointOfSale.Infrastructure/Services/MarketplaceReviews/MarketplaceProductReviewBackboneServices.cs");
        var consumers = ReadRepoFile("PointOfSale.Infrastructure/Consumers/Marketplace/MarketplaceReviewConsumers.cs");

        Assert.Contains("review.Status != ProductReviewStatus.Pending", backbone, StringComparison.Ordinal);
        Assert.Contains("review.Approve()", backbone, StringComparison.Ordinal);
        Assert.Contains("ReconcileReviewProjectionAsync", backbone, StringComparison.Ordinal);
        Assert.Contains("review is { IsDeleted: false, Status: ProductReviewStatus.Approved }", backbone, StringComparison.Ordinal);
        Assert.Contains("await RemoveReviewDocumentAsync(reviewId", backbone, StringComparison.Ordinal);
        Assert.Contains("await RebuildAggregatesAsync(productId, eventId", backbone, StringComparison.Ordinal);
        Assert.Contains("BuildSummaryDocumentAsync", backbone, StringComparison.Ordinal);
        Assert.Contains("review.Status == ProductReviewStatus.Approved", backbone, StringComparison.Ordinal);
        Assert.Contains("MarketplaceIdempotentConsumer", consumers, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogSearchRatingInputs_MustUseOnlySqlApprovedVisibleReviewAuthority()
    {
        var overlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");
        var backbone = ReadRepoFile("PointOfSale.Infrastructure/Services/MarketplaceReviews/MarketplaceProductReviewBackboneServices.cs");

        Assert.Contains("approvedReviewRatings", overlay, StringComparison.Ordinal);
        Assert.Contains("review.Status == ProductReviewStatus.Approved", overlay, StringComparison.Ordinal);
        Assert.Contains("!review.IsDeleted", overlay, StringComparison.Ordinal);
        Assert.Contains("document.ReviewCount = approvedReviewRatings.Count", overlay, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", backbone, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync", backbone, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchDocumentMapper.Map(sqlAuthority)", backbone, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocs_MustRecordM9AuthorityFreezeAndFindings()
    {
        var findings = ReadRepoFile("docs/architecture/commercehub-reviews-ratings-authority-certification-findings.md");

        Assert.Contains("CommerceHub Reviews & Ratings Authority Certification Findings", findings, StringComparison.Ordinal);
        Assert.Contains("production-certifiable", findings, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
