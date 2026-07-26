namespace PointOfSale.Tests.Architecture;

public sealed class SuperAdminRecoverySafetyCertificationTests
{
    [Fact]
    public void TenantAdminCommerceHubSearchRecovery_MustStayTenantScoped()
    {
        var controller = File.ReadAllText(ApiPath("Controllers", "AdminCommerceHubController.cs"));
        var service = File.ReadAllText(InfrastructurePath("Jobs", "CommerceHubReindexService.cs"));

        Assert.Contains("_tenantContext.RequiredTenantId", controller, StringComparison.Ordinal);
        Assert.Contains("_reindexService.ReindexTenantAsync(tenantId", controller, StringComparison.Ordinal);
        Assert.Contains("GLOBAL_SEARCH_CONTROL_FORBIDDEN", controller, StringComparison.Ordinal);
        Assert.Contains("global platform recovery primitive", controller, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Task<CommerceHubReindexResult> ReindexTenantAsync(Guid tenantId", service, StringComparison.Ordinal);
        Assert.Contains("Builders<CommerceHubProductDocument>.Filter.Eq(document => document.TenantId, tenantId)", service, StringComparison.Ordinal);
        Assert.Contains("query = query.Where(listing => listing.TenantId == tenantId.Value)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("var result = await _reindexService.ReindexAsync(cancellationToken);", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_searchIndexService.FinalizeAliasMigrationAsync(", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_reindexService.CatchUpReindexAsync(", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalCommerceHubReindex_MustBeSqlListingAuthoritativeAndOperationallyPaced()
    {
        var overlay = File.ReadAllText(InfrastructurePath("Elasticsearch", "Services", "CommerceHubSearchSqlAuthorityOverlay.cs"));
        var service = File.ReadAllText(InfrastructurePath("Jobs", "CommerceHubReindexService.cs"));
        var options = File.ReadAllText(InfrastructurePath("Jobs", "CommerceHubReindexOptions.cs"));
        var program = File.ReadAllText(ApiPath("Program.cs"));

        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", overlay, StringComparison.Ordinal);
        Assert.Contains("listing.IsPublished", overlay, StringComparison.Ordinal);
        Assert.Contains("item.IsActive &&", overlay, StringComparison.Ordinal);
        Assert.Contains("WherePubliclyVisibleLineage(tenantId)", overlay, StringComparison.Ordinal);

        Assert.Contains("IOptions<CommerceHubReindexOptions>", service, StringComparison.Ordinal);
        Assert.Contains("_options.BoundedParallelBatchSize", service, StringComparison.Ordinal);
        Assert.Contains("_options.BoundedSearchSyncStatusBatchSize", service, StringComparison.Ordinal);
        Assert.Contains("DelayBetweenBatchesAsync", service, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(ParallelBatchSize, 1, 32)", options, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(InterBatchDelayMilliseconds, 0, 60000)", options, StringComparison.Ordinal);
        Assert.Contains("AddOptions<PointOfSale.Infrastructure.Jobs.CommerceHubReindexOptions>()", program, StringComparison.Ordinal);
        Assert.Contains("ValidateOnStart()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryDocumentation_MustRecordPhase3ReplayRebuildFindings()
    {
        var superAdminFindings = File.ReadAllText(DocsPath("architecture", "super-admin-platform-authority-certification-findings.md"));
        var marketplaceFindings = File.ReadAllText(DocsPath("architecture", "marketplace-governance-certification-findings.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));

        foreach (var marker in new[]
        {
            "Phase 3 Replay / Rebuild / Recovery Safety Addendum",
            "replay amplification",
            "tenant-scoped CommerceHub search reindex",
            "rebuild interruption",
            "remaining recovery-risk debt"
        })
        {
            Assert.Contains(marker, superAdminFindings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Phase 3 Recovery Governance Addendum", marketplaceFindings, StringComparison.Ordinal);
        Assert.Contains("Replay/Rebuild Overlap Semantics", runbook, StringComparison.Ordinal);
        Assert.Contains("SUPERADMIN-CON-01 Phase 3 Recovery Freeze", primitives, StringComparison.Ordinal);
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
