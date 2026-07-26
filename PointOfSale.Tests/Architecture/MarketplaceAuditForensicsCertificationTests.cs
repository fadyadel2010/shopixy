namespace PointOfSale.Tests.Architecture;

public sealed class MarketplaceAuditForensicsCertificationTests
{
    [Fact]
    public void Pass155ForensicEvidence_MustRemainAppendOnlyInsideTheSqlBoundary()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("GuardForensicEvidenceImmutability();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<AuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubFinancialAuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceHubVendorPaymentAuditLog>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<CommerceAccountingAudit>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<StoreConfigAuditEntry>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<LedgerEntry>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("GuardAppendOnlyEvidence<MarketplaceEventOutboxDeadLetterArchive>();", dbContext, StringComparison.Ordinal);
        Assert.Contains("forensic evidence records are append-only and cannot be deleted", dbContext, StringComparison.Ordinal);
        Assert.Contains("forensic evidence records are append-only and cannot be updated", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155SuperAdminAudit_MustPersistDurablyWithoutTheProcessMemoryQueue()
    {
        var auditService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminAuditService.cs");

        Assert.Contains("IServiceScopeFactory", auditService, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ApplicationDbContext>()", auditService, StringComparison.Ordinal);
        Assert.Contains("dbContext.AuditLogs.AddAsync", auditService, StringComparison.Ordinal);
        Assert.Contains("dbContext.SaveChangesAsync", auditService, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditQueue", auditService, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass155CertificationDocs_MustRecordEvidenceInventoryAndBlockingGaps()
    {
        var findings = ReadRepoFile("docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");

        foreach (var marker in new[]
        {
            "PASS 15.5 Marketplace Audit, Compliance & Forensics Certification",
            "Evidence Inventory",
            "Operator Accountability Findings",
            "Merchant Accountability Findings",
            "Governance Evidence Findings",
            "Financial Evidence Findings",
            "Recovery Evidence Findings",
            "Reconstruction Findings",
            "Audit Integrity Findings",
            "Retention Findings",
            "Searchability Findings",
            "Frontend / Backend Alignment Findings",
            "Incident Forensics Findings",
            "Chaos Findings",
            "Monitoring Findings",
            "Can money movement be reconstructed? Yes for certified SQL/ledger flows.",
            "Is evidence retention sufficient? No.",
            "Are there critical forensic gaps? Yes.",
            "Is CommerceHub audit/compliance/forensics production-ready? No."
        })
        {
            Assert.Contains(marker, findings, StringComparison.Ordinal);
        }

        Assert.Contains("PASS 15.5 Marketplace Audit, Compliance & Forensics", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 15.5 Marketplace Audit, Compliance & Forensics Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 15.5 marketplace forensic readiness remains blocked", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 15.5 Marketplace Audit, Compliance & Forensics Freeze", codexRules, StringComparison.Ordinal);
        Assert.Contains("PASS 15.5 Marketplace Audit, Compliance & Forensics", hardening, StringComparison.Ordinal);
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
