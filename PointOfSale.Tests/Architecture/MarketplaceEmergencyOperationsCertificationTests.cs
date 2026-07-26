namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceEmergencyOperationsCertificationTests
{
    [Fact]
    public void Pass154EmergencyFinanceStops_MustRemainInsideReadinessGate()
    {
        AssertSourceContains(
            ApiPath("Configuration", "CommerceHubEmergencyStopOptions.cs"),
            "CommerceHub:EmergencyStops",
            "CommerceIntakeStopped",
            "PaymentIntakeStopped",
            "OutwardMoneyMovementStopped",
            "PayoutsStopped",
            "RefundsStopped");

        AssertSourceContains(
            ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialReadinessGate.cs"),
            "IOptionsMonitor<CommerceHubEmergencyStopOptions>",
            "EnsureEmergencyOutwardMoneyMovementAllowed",
            "EnsureEmergencyCommerceIntakeAllowed",
            "COMMERCEHUB_EMERGENCY_MONEY_MOVEMENT_STOPPED",
            "COMMERCEHUB_EMERGENCY_PAYMENT_INTAKE_STOPPED",
            "StatusCodes.Status503ServiceUnavailable");

        AssertSourceContains(
            ApiPath("Program.cs"),
            "CommerceHubEmergencyStopOptions.SectionName");
    }

    [Fact]
    public void Pass154EmergencyVisibilityRecoveryAndAudit_MustRemainSqlAuthorityBacked()
    {
        AssertSourceContains(
            ApiPath("SuperAdmin", "Controllers", "SuperAdminMarketplaceGovernanceController.cs"),
            "HideByGovernance",
            "GetRestoreAuthorityFailureAsync",
            "ClearGovernanceRestoreState",
            "EnqueueListingVerification",
            "MARKETPLACE_LISTING_SUPPRESSED",
            "MARKETPLACE_TENANT_SUSPENDED");

        AssertSourceContains(
            ApiPath("SuperAdmin", "Controllers", "SuperAdminMarketplaceOperationsController.cs"),
            "ValidateReason",
            "ReindexAsync",
            "ReindexTenantAsync",
            "CatchUpReindexAsync",
            "CommerceHubAutoHealingTask",
            "LogOperationsActionAsync");

        AssertSourceContains(
            InfrastructurePath("Jobs", "CommerceHubReindexService.cs"),
            "CommerceHubSearchSqlAuthorityOverlay.ApplyAsync",
            "DeleteStaleSearchDocumentsAsync",
            "DelayBetweenBatchesAsync",
            "cancellationToken.ThrowIfCancellationRequested");
    }

    [Fact]
    public void Pass154EmergencyCertificationDocs_MustRecordIncidentDayAnswers()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "commercehub-emergency-operations-certification-findings.md"));
        var projectContext = File.ReadAllText(ContextPath("PROJECT_CONTEXT.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var knownRisks = File.ReadAllText(ContextPath("KNOWN_RISKS.md"));
        var codexRules = File.ReadAllText(ContextPath("CODEX_RULES.md"));
        var hardening = File.ReadAllText(ContextPath("CROSS_SYSTEM_AUTHORITY_HARDENING.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));

        foreach (var marker in new[]
        {
            "PASS 15.4 Emergency Operations & Incident Response Certification",
            "Emergency Inventory",
            "Break-Glass Findings",
            "Visibility Findings",
            "Finance Emergency Findings",
            "Recovery Stop Findings",
            "Rollback Findings",
            "Incident Diagnostics Findings",
            "Runbook Findings",
            "Emergency Audit Findings",
            "Frontend / Backend Alignment Findings",
            "Operator Safety Findings",
            "Finance Incident Findings",
            "Chaos Findings",
            "Monitoring Findings",
            "Can operators stop marketplace visibility safely? Yes.",
            "Can operators stop outward money movement safely? Yes.",
            "Can operators stop replay/recovery safely? Partially.",
            "Is CommerceHub incident response production-ready? Yes, with operational maturity debt."
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 15.4 CommerceHub Emergency Operations", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 15.4 CommerceHub Emergency Operations Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("CommerceHub emergency operations are now certified", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 15.4 CommerceHub Emergency Operations Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 15.4 CommerceHub Emergency Operations", hardening, StringComparison.Ordinal);
        Assert.Contains("CommerceHub Emergency Operations Incident-Day Controls", runbook, StringComparison.Ordinal);
    }

    private static void AssertSourceContains(string path, params string[] markers)
    {
        var source = File.ReadAllText(path);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    private static string ApiPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.API", .. parts]);

    private static string InfrastructurePath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Infrastructure", .. parts]);

    private static string DocsPath(params string[] parts) =>
        Path.Combine([DiscoverSolutionRoot(AppContext.BaseDirectory), "docs", .. parts]);

    private static string ContextPath(string fileName) =>
        Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), ".ai-context", fileName);

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
