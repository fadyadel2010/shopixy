namespace PointOfSale.Tests.Architecture;

public sealed class EvidenceDatabaseHardeningCertificationTests
{
    private static readonly string[] EvidenceTables =
    [
        "AuditLogs",
        "UserGovernanceAuditLogs",
        "CommerceHubFinancialTransactions",
        "CommerceHubFinancialAuditLogs",
        "CommerceHubFinancialOperationAudits",
        "CommerceHubFinancialReconciliationReports",
        "CommerceHubVendorSettlements",
        "CommerceHubVendorPaymentAuditLogs",
        "LedgerEntries",
        "CommerceAccountingAudits",
        "StoreConfigAuditEntries",
        "MarketplaceEventOutboxDeadLetterArchives",
        "CommerceHubMerchantLifecycleLedgerEntries",
        "CommerceHubRecoveryOperationLedgerEntries"
    ];

    [Fact]
    public void Closure1Migration_MustInstallDatabaseAppendOnlyTriggersAndFailClosedOnRollback()
    {
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260601190000_EvidenceDatabaseHardening.cs");

        Assert.Contains("CREATE OR REPLACE FUNCTION forensic.reject_append_only_evidence_mutation()", migration, StringComparison.Ordinal);
        Assert.Contains("BEFORE UPDATE OR DELETE OR TRUNCATE", migration, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_VIOLATION", migration, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_INVENTORY_DRIFT", migration, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_ROLLBACK_PROHIBITED", migration, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON SCHEMA forensic FROM PUBLIC", migration, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON FUNCTION forensic.reject_append_only_evidence_mutation() FROM PUBLIC", migration, StringComparison.Ordinal);

        foreach (var table in EvidenceTables)
        {
            Assert.Contains($"'{table}'", migration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Closure1RoleProvisioning_MustSeparateRuntimeWriterReaderMigrationAndMaintenanceAuthority()
    {
        var script = ReadRepoFile("PointOfSale.Infrastructure/Scripts/ProvisionForensicEvidenceRoles.sql");

        Assert.Contains("CREATE ROLE pointofsale_evidence_owner NOLOGIN NOINHERIT", script, StringComparison.Ordinal);
        Assert.Contains("CREATE ROLE pointofsale_evidence_writer NOLOGIN INHERIT", script, StringComparison.Ordinal);
        Assert.Contains("CREATE ROLE pointofsale_evidence_reader NOLOGIN INHERIT", script, StringComparison.Ordinal);
        Assert.Contains("CREATE ROLE pointofsale_evidence_migration NOLOGIN NOINHERIT", script, StringComparison.Ordinal);
        Assert.Contains("CREATE ROLE pointofsale_evidence_maintenance NOLOGIN INHERIT", script, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT ON TABLE", script, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON TABLE", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.%I OWNER TO pointofsale_evidence_owner", script, StringComparison.Ordinal);
        Assert.Contains("ALTER SCHEMA forensic OWNER TO pointofsale_evidence_owner", script, StringComparison.Ordinal);
        Assert.Contains("current_setting('pointofsale.application_login')", script, StringComparison.Ordinal);
        Assert.Contains("current_setting('pointofsale.background_worker_login')", script, StringComparison.Ordinal);
        Assert.Contains("current_setting('pointofsale.reporting_login')", script, StringComparison.Ordinal);
        Assert.Contains("current_setting('pointofsale.maintenance_login')", script, StringComparison.Ordinal);
        Assert.Contains("current_setting('pointofsale.migration_login')", script, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_ROLE_SEPARATION_VIOLATION", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT UPDATE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT DELETE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT TRUNCATE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Closure1Verification_MustProbePrivilegeDriftTriggersAndDirectSqlMutationFailure()
    {
        var script = ReadRepoFile("PointOfSale.Infrastructure/Scripts/VerifyForensicEvidenceDatabaseHardening.sql");

        Assert.Contains("writer_can_insert", script, StringComparison.Ordinal);
        Assert.Contains("writer_can_update", script, StringComparison.Ordinal);
        Assert.Contains("writer_can_delete", script, StringComparison.Ordinal);
        Assert.Contains("writer_can_truncate", script, StringComparison.Ordinal);
        Assert.Contains("append_only_trigger_installed", script, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_VERIFICATION_FAILED", script, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_UPDATE_PROBE_FAILED", script, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_DELETE_PROBE_FAILED", script, StringComparison.Ordinal);
        Assert.Contains("aclexplode", script, StringComparison.Ordinal);
        Assert.Contains("forensic schema owner drift", script, StringComparison.Ordinal);
        Assert.Contains("rejection function owner drift", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Closure1EvidenceInventory_MustStayAlignedAcrossMigrationProvisioningAndVerification()
    {
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260601190000_EvidenceDatabaseHardening.cs");
        var provisioning = ReadRepoFile("PointOfSale.Infrastructure/Scripts/ProvisionForensicEvidenceRoles.sql");
        var verification = ReadRepoFile("PointOfSale.Infrastructure/Scripts/VerifyForensicEvidenceDatabaseHardening.sql");

        foreach (var table in EvidenceTables)
        {
            Assert.Contains($"'{table}'", migration, StringComparison.Ordinal);
            Assert.Contains($"'{table}'", provisioning, StringComparison.Ordinal);
            Assert.Contains($"('{table}')", verification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Closure1Documentation_MustKeepDeploymentExecutionAndRemainingLaunchBlockersExplicit()
    {
        var findings = ReadRepoFile("docs/architecture/evidence-database-hardening-certification-findings.md");

        Assert.Contains("PASS 15.6 Launch Blocker #1 is eliminated after deployment verification", findings, StringComparison.Ordinal);
        Assert.Contains("Application role", findings, StringComparison.Ordinal);
        Assert.Contains("Background worker role", findings, StringComparison.Ordinal);
        Assert.Contains("Migration role", findings, StringComparison.Ordinal);
        Assert.Contains("Maintenance role", findings, StringComparison.Ordinal);
        Assert.Contains("Reporting role", findings, StringComparison.Ordinal);
        Assert.Contains("FORENSIC_APPEND_ONLY_VIOLATION", findings, StringComparison.Ordinal);
        Assert.Contains("external WORM/SIEM", findings, StringComparison.Ordinal);
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
