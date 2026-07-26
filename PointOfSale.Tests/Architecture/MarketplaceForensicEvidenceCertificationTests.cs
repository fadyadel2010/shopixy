namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceForensicEvidenceCertificationTests
{
    [Fact]
    public void Pass155BMerchantLifecycleLedger_MustRemainAppendOnlyAndCoverMerchantMutationPaths()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var adminController = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var governanceController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");

        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubMerchantLifecycleLedgerEntry>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("\"Publish\"", handlers, StringComparison.Ordinal);
        Assert.Contains("\"PublishOrUpdate\"", handlers, StringComparison.Ordinal);
        Assert.Contains("\"Update\"", handlers, StringComparison.Ordinal);
        Assert.Contains("\"Unpublish\"", handlers, StringComparison.Ordinal);
        Assert.Contains("$\"Bulk:{operation}\"", adminController, StringComparison.Ordinal);
        Assert.Contains("\"GovernanceSuppression\"", governanceController, StringComparison.Ordinal);
        Assert.Contains("\"GovernanceRestore\"", governanceController, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155BRecoveryLedger_MustCaptureOperatorAndBackgroundRecovery()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var operationsController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceOperationsController.cs");
        var autoHealing = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs");

        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubRecoveryOperationLedgerEntry>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("\"Reindex\"", operationsController, StringComparison.Ordinal);
        Assert.Contains("\"CatchUpReindex\"", operationsController, StringComparison.Ordinal);
        Assert.Contains("\"VerificationEnqueue\"", operationsController, StringComparison.Ordinal);
        Assert.Contains("\"AutoHealing\"", autoHealing, StringComparison.Ordinal);
        Assert.Contains("PersistRecoveryEvidenceAsync", autoHealing, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155BTenantAudit_MustPersistSqlFirstAndExposeRetryBacklog()
    {
        var auditService = ReadRepoFile("PointOfSale.API/Services/Audit/AuditService.cs");
        var auditQueue = ReadRepoFile("PointOfSale.API/Services/Audit/AuditQueue.cs");
        var backgroundService = ReadRepoFile("PointOfSale.API/Services/Audit/AuditLogBackgroundService.cs");

        Assert.Contains("GetRequiredService<ApplicationDbContext>()", auditService, StringComparison.Ordinal);
        Assert.Contains("dbContext.AuditLogs.AddAsync", auditService, StringComparison.Ordinal);
        Assert.Contains("_queue.Enqueue(item)", auditService, StringComparison.Ordinal);
        Assert.Contains("QueueDepth", auditQueue, StringComparison.Ordinal);
        Assert.Contains("RetryCount", auditQueue, StringComparison.Ordinal);
        Assert.Contains("_queue.Retry(logItem)", backgroundService, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155BForensicApi_MustExposeSearchExportRetentionAndHealth()
    {
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceForensicsController.cs");

        Assert.Contains("[HttpGet(\"search\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"export\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"retention\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"health\")]", controller, StringComparison.Ordinal);
        Assert.Contains("ActorUserId", controller, StringComparison.Ordinal);
        Assert.Contains("TenantId", controller, StringComparison.Ordinal);
        Assert.Contains("ListingId", controller, StringComparison.Ordinal);
        Assert.Contains("OrderId", controller, StringComparison.Ordinal);
        Assert.Contains("RefundId", controller, StringComparison.Ordinal);
        Assert.Contains("PayoutId", controller, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", controller, StringComparison.Ordinal);
        Assert.Contains("RequestId", controller, StringComparison.Ordinal);
        Assert.Contains("OperationId", controller, StringComparison.Ordinal);
        Assert.Contains("Configure the receiving SIEM or object store with WORM retention.", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155BCertificationDocs_MustBoundRemainingDeploymentDebt()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-forensic-evidence-hardening-certification-findings.md");
        Assert.Contains("PASS 15.5B Forensic Evidence Hardening Certification", findings, StringComparison.Ordinal);
        Assert.Contains("Can merchant lifecycle be reconstructed? Yes", findings, StringComparison.Ordinal);
        Assert.Contains("Can recovery incidents be reconstructed? Yes for the certified CommerceHub paths", findings, StringComparison.Ordinal);
        Assert.Contains("Are any critical forensic gaps still present? Yes", findings, StringComparison.Ordinal);
        Assert.Contains("Is CommerceHub forensic readiness production-ready? No", findings, StringComparison.Ordinal);
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
