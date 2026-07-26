namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCodRiskCertificationTests
{
    [Fact]
    public void CodRiskCertification_MustStayInventoriedAndDocumented()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-cod-risk-control-certification-findings.md");

        foreach (var marker in new[]
        {
            "Scope: PASS 16.5C",
            "## Risk Inventory",
            "## Debt Accumulation Findings",
            "## Debt Reduction Findings",
            "## Threshold Findings",
            "## Suspension Findings",
            "## Reactivation Findings",
            "## Checkout Findings",
            "## Discovery Findings",
            "## Existing-Order Findings",
            "## Concurrency Findings",
            "## Recovery Findings",
            "## Dashboard Findings",
            "## Auditability Findings",
            "## Chaos Findings",
            "## Monitoring Findings",
            "## Runtime Hardening Performed",
            "## Regression Freeze Added",
            "## Architecture Updates",
            "## Remaining Debt",
            "## Required Answers"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CodRiskCertificationDocumentation_MustRecordPassSixteenFiveC()
    {
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var hardening = ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        Assert.Contains("PASS 16.5C COD Risk & Debt Control Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 16.5C COD Risk & Debt Control Certification Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("PASS 16.5C CommerceHub COD risk-control runtime certifiable", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 16.5C COD Risk & Debt Control Certification", hardening, StringComparison.Ordinal);
        Assert.Contains("PASS 16.5C COD Risk & Debt Control Certification Freeze", codexRules, StringComparison.Ordinal);
    }

    [Fact]
    public void CodRiskControl_MustKeepCheckoutDiscoveryRecoveryAndDashboardAuthorityDocumented()
    {
        var certification = ReadRepoFile("docs/architecture/commercehub-cod-risk-control-certification-findings.md");

        Assert.Contains("`CommerceHubCheckoutService`", certification, StringComparison.Ordinal);
        Assert.Contains("`CommerceHubPublicVendorVisibilityService`", certification, StringComparison.Ordinal);
        Assert.Contains("`CommerceHubQueryService`", certification, StringComparison.Ordinal);
        Assert.Contains("Recovery and replay are SQL-first", certification, StringComparison.Ordinal);
        Assert.Contains("Dashboard counters are not authority", certification, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL-backed chaos tests", certification, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
