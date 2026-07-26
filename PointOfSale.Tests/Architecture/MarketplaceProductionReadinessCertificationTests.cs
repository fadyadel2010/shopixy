namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceProductionReadinessCertificationTests
{
    [Fact]
    public void Pass156LaunchVerdict_MustRemainNotReadyUntilDeploymentBlockersAreClosed()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-production-readiness-certification-findings.md");

        Assert.Contains("PASS 15.6 Marketplace Production Readiness Certification", findings, StringComparison.Ordinal);
        Assert.Contains("**NOT READY**", findings, StringComparison.Ordinal);
        Assert.Contains("### A) Launch Blockers", findings, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL roles", findings, StringComparison.Ordinal);
        Assert.Contains("external SIEM or object storage with WORM retention", findings, StringComparison.Ordinal);
        Assert.Contains("outage-surviving durable handoff", findings, StringComparison.Ordinal);
        Assert.Contains("launch alert packs and dashboards", findings, StringComparison.Ordinal);
        Assert.Contains("backups, archive retention, restore rehearsal", findings, StringComparison.Ordinal);
        Assert.Contains("Is CommerceHub production ready? No.", findings, StringComparison.Ordinal);
        Assert.Contains("Is CommerceHub launch ready? No.", findings, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass156RuntimeAuthorityAssumptions_MustRemainFailClosed()
    {
        var appSettings = ReadRepoFile("PointOfSale.API/appsettings.json");
        var auditService = ReadRepoFile("PointOfSale.API/Services/Audit/AuditService.cs");
        var forensicsController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceForensicsController.cs");
        var emergencyGate = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialReadinessGate.cs");
        var commerceHubOverlay = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchSqlAuthorityOverlay.cs");

        Assert.Contains("\"Enabled\": false", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"CommerceIntakeStopped\": false", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"OutwardMoneyMovementStopped\": false", appSettings, StringComparison.Ordinal);
        Assert.Contains("_queue.Enqueue(item)", auditService, StringComparison.Ordinal);
        Assert.Contains("Configure the receiving SIEM or object store with WORM retention.", forensicsController, StringComparison.Ordinal);
        Assert.Contains("EnsureEmergencyCommerceIntakeAllowed", emergencyGate, StringComparison.Ordinal);
        Assert.Contains("EnsureEmergencyOutwardMoneyMovementAllowed", emergencyGate, StringComparison.Ordinal);
        Assert.Contains("SqlAuthorityOverlayKind.CommerceHubListing", commerceHubOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass156ArchitectureRecords_MustFreezeTheFinalVerdict()
    {
        Assert.Contains("PASS 15.6 Marketplace Production Readiness Certification", ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 15.6 Marketplace Production Readiness Certification Truth", ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 15.6 marketplace public launch remains blocked", ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 15.6 Marketplace Production Readiness Certification Freeze", ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 15.6 Marketplace Production Readiness Certification", ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
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

