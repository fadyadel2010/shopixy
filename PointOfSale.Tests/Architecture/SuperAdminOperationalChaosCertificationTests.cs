namespace PointOfSale.Tests.Architecture;

public sealed class SuperAdminOperationalChaosCertificationTests
{
    [Fact]
    public void MarketplaceReviewModerationReplay_MustNotResurrectSearchFromStaleDerivativeListingState()
    {
        var reviewBackbone = File.ReadAllText(InfrastructurePath(
            "Services",
            "MarketplaceReviews",
            "MarketplaceProductReviewBackboneServices.cs"));

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reviewBackbone, StringComparison.Ordinal);
        Assert.Contains("document.TenantId", reviewBackbone, StringComparison.Ordinal);
        Assert.Contains("_searchIndexService.DeleteAsync(", reviewBackbone, StringComparison.Ordinal);
        Assert.Contains("CommerceHubSearchDocumentMapper.Map(sqlAuthority)", reviewBackbone, StringComparison.Ordinal);
        Assert.Contains("Builders<CommerceHubProductDocument>.Filter.Eq(item => item.TenantId, document.TenantId)", reviewBackbone, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubSearchDocumentMapper.Map(document)", reviewBackbone, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRebuildOverlapFreeze_MustKeepNormalRetryPathsSqlAuthoritativeAndBounded()
    {
        var homepageConsumer = File.ReadAllText(InfrastructurePath("Consumers", "Marketplace", "HomepageProjectionConsumer.cs"));
        var reindexService = File.ReadAllText(InfrastructurePath("Jobs", "CommerceHubReindexService.cs"));
        var eventOutboxProcessor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceEventOutboxProcessor.cs"));
        var projectionOutboxProcessor = File.ReadAllText(InfrastructurePath("Eventing", "MarketplaceProjectionOutboxProcessor.cs"));

        AssertSourceOrder(
            homepageConsumer,
            "ProjectionConsumerEventFence.TryAcquireAsync",
            "_projectionService.RebuildAsync",
            "Homepage replay must be fenced before rebuild side effects.");
        AssertSourceOrder(
            homepageConsumer,
            "_projectionService.RebuildAsync",
            "fence.CompleteAsync",
            "Homepage replay fence must complete only after projection convergence.");

        Assert.Contains("CommerceHubSearchSqlAuthorityOverlay.ApplyAsync", reindexService, StringComparison.Ordinal);
        Assert.Contains("ReconcileMissingSqlListingsAsync", reindexService, StringComparison.Ordinal);
        Assert.Contains("query = query.Where(listing => listing.TenantId == tenantId.Value)", reindexService, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(seed.TenantId, seed.ListingId", reindexService, StringComparison.Ordinal);
        Assert.Contains("DelayBetweenBatchesAsync", reindexService, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested", reindexService, StringComparison.Ordinal);

        Assert.Contains("RetryFailedMessageAsync", eventOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("message.ResetForRetry()", eventOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("ProcessPendingMessagesAsync", eventOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("!message.IsFailed", eventOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc == null || message.ClaimedUntilUtc <= DateTime.UtcNow", eventOutboxProcessor, StringComparison.Ordinal);

        Assert.Contains("ProcessPendingMessagesAsync", projectionOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc", projectionOutboxProcessor, StringComparison.Ordinal);
        Assert.Contains("DeleteOutboxResidueAfterDuplicateCompletionAsync", projectionOutboxProcessor, StringComparison.Ordinal);
    }

    [Fact]
    public void EmergencyAuthorityAndDangerousPrimitiveFreeze_MustRemainAuditedFailClosedAndDocumented()
    {
        var platformAuthority = File.ReadAllText(ApiPath("SuperAdmin", "Infrastructure", "SuperAdminPlatformAuthority.cs"));
        var auditMiddleware = File.ReadAllText(ApiPath("SuperAdmin", "Infrastructure", "SuperAdminOperationalAuditMiddleware.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var dangerousGuard = File.ReadAllText(InfrastructurePath("Reliability", "DerivativeProjectionMutationGuard.cs"));

        Assert.Contains("SuperAdminSecurityConstants.TokenVersionClaim", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("STALE_SUPER_ADMIN_TOKEN", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_GOVERNANCE_REJECTED", platformAuthority, StringComparison.Ordinal);
        Assert.Contains("OperationalConvergenceAuthority.PlatformAdmin", platformAuthority, StringComparison.Ordinal);

        Assert.Contains("AuditedMethods.Contains(context.Request.Method)", auditMiddleware, StringComparison.Ordinal);
        Assert.Contains("PLATFORM_OPERATION_COMPLETED", auditMiddleware, StringComparison.Ordinal);
        Assert.Contains("SuperAdminPlatformAuthority.SuperAdminId", auditMiddleware, StringComparison.Ordinal);
        AssertSourceOrder(
            program,
            "SuperAdminPlatformAuthorityMiddleware",
            "SuperAdminOperationalAuditMiddleware",
            "Operational audit must run after current SQL platform authority recomposition.");

        Assert.Contains("DERIVATIVE_MUTATION_SQL_AUTHORITY_REQUIRED", dangerousGuard, StringComparison.Ordinal);
        Assert.Contains("DERIVATIVE_MUTATION_FRESHNESS_GUARD_REQUIRED", dangerousGuard, StringComparison.Ordinal);
        Assert.Contains("DERIVATIVE_MUTATION_FAIL_CLOSED_REQUIRED", dangerousGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase5CertificationDocumentation_MustRecordFindingsDebtAndRegressionFreeze()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "super-admin-operational-chaos-certification-findings.md"));
        var superAdminFindings = File.ReadAllText(DocsPath("architecture", "super-admin-platform-authority-certification-findings.md"));
        var marketplaceFindings = File.ReadAllText(DocsPath("architecture", "marketplace-governance-certification-findings.md"));
        var emergencyFindings = File.ReadAllText(DocsPath("architecture", "emergency-governance-certification-findings.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));

        foreach (var marker in new[]
        {
            "SUPERADMIN-CON-01 Phase 5",
            "Chaos Scenarios Certified",
            "Replay/Rebuild Convergence Guarantees",
            "Operational Misuse Findings",
            "Marketplace Chaos-Convergence Findings",
            "Remaining Chaos-Risk Debt",
            "Regression Freeze Invariants",
            "marketplace review aggregate replay",
            "fail closed"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Phase 5 Operational Chaos Certification Addendum", superAdminFindings, StringComparison.Ordinal);
        Assert.Contains("Phase 5 Operational Chaos Addendum", marketplaceFindings, StringComparison.Ordinal);
        Assert.Contains("Phase 5 Operational Chaos Addendum", emergencyFindings, StringComparison.Ordinal);
        Assert.Contains("Super-Admin Operational Chaos Certification", runbook, StringComparison.Ordinal);
        Assert.Contains("SUPERADMIN-CON-01 Phase 5 Chaos Certification Freeze", primitives, StringComparison.Ordinal);
    }

    private static void AssertSourceOrder(string source, string first, string second, string message)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Missing source marker '{first}'.");
        Assert.True(secondIndex >= 0, $"Missing source marker '{second}'.");
        Assert.True(firstIndex < secondIndex, message);
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
