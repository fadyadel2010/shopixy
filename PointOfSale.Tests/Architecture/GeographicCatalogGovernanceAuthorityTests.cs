namespace PointOfSale.Tests.Architecture;

public sealed class GeographicCatalogGovernanceAuthorityTests
{
    [Fact]
    public void Geo3GovernanceDocument_MustExistAndRemainArchitectureOnly()
    {
        var geo3 = CertificationSource.ReadRepoFile("docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md");

        Assert.Contains("Status: Governance architecture freeze pending approval", geo3, StringComparison.Ordinal);
        Assert.Contains("Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`", geo3, StringComparison.Ordinal);
        Assert.Contains("Runtime impact: none", geo3, StringComparison.Ordinal);
        Assert.Contains("Backend impact: none", geo3, StringComparison.Ordinal);
        Assert.Contains("Frontend impact: none", geo3, StringComparison.Ordinal);
        Assert.Contains("Checkout impact: none", geo3, StringComparison.Ordinal);
        Assert.Contains("Shipping runtime impact: none", geo3, StringComparison.Ordinal);
        Assert.Contains("Carrier adapter impact: none", geo3, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo3AuthorityMatrix_MustAssignPlatformAdministrationOnly()
    {
        var geo3 = CertificationSource.ReadRepoFile("docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md");

        foreach (var requiredAuthority in new[]
                 {
                     "Who creates Places?",
                     "Who edits Places?",
                     "Who retires Places?",
                     "Who merges Places?",
                     "Who splits Places?",
                     "Who approves catalog changes?",
                     "Who publishes catalog Releases?",
                     "Who rolls back catalog Releases?",
                     "Who imports Provider catalogs?",
                     "Who reviews Sync Candidates?",
                     "Who approves Mapping Releases?",
                     "Who creates Overrides?",
                     "Who approves Overrides?",
                     "Who owns Country Profiles?",
                     "Who owns Localization?",
                     "Who owns Aliases?",
                     "Who owns Search labels?"
                 })
        {
            Assert.Contains(requiredAuthority, geo3, StringComparison.Ordinal);
        }

        Assert.Contains("All four workspaces are Platform Admin only.", geo3, StringComparison.Ordinal);
        Assert.Contains("Forbidden authorities:", geo3, StringComparison.Ordinal);
        Assert.Contains("Providers cannot mutate catalog or mapping authority directly.", geo3, StringComparison.Ordinal);
        Assert.Contains("Carrier adapters cannot mutate catalog", geo3, StringComparison.Ordinal);
        Assert.Contains("Checkout, Storefront, Orders, Shipments", geo3, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo3Workflow_MustFreezeApprovalPublicationRollbackAndSyncControls()
    {
        var geo3 = CertificationSource.ReadRepoFile("docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md");

        Assert.Contains("Catalog Draft", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Validation", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Review", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Approval", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Publication", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Activation", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Supersede", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Rollback", geo3, StringComparison.Ordinal);
        Assert.Contains("-> Archive", geo3, StringComparison.Ordinal);

        Assert.Contains("Four-eyes approval is mandatory", geo3, StringComparison.Ordinal);
        Assert.Contains("Publication locking", geo3, StringComparison.Ordinal);
        Assert.Contains("Rollback is an activation operation, not an edit.", geo3, StringComparison.Ordinal);
        Assert.Contains("Provider synchronization is evidence intake, not publication.", geo3, StringComparison.Ordinal);
        Assert.Contains("Provider sync must never publish automatically.", geo3, StringComparison.Ordinal);
        Assert.Contains("Runtime carrier mapping resolution consumes only published mapping releases.", geo3, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo3ValidationAuditAndSecurity_MustFailClosed()
    {
        var geo3 = CertificationSource.ReadRepoFile("docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md");

        Assert.Contains("Security Model", geo3, StringComparison.Ordinal);
        Assert.Contains("Least privilege separates editor, reviewer, approver", geo3, StringComparison.Ordinal);
        Assert.Contains("Approval separation prevents self-approval", geo3, StringComparison.Ordinal);
        Assert.Contains("Country isolation", geo3, StringComparison.Ordinal);
        Assert.Contains("Provider isolation", geo3, StringComparison.Ordinal);
        Assert.Contains("Tenant isolation", geo3, StringComparison.Ordinal);

        Assert.Contains("Audit is append-only and mandatory.", geo3, StringComparison.Ordinal);
        Assert.Contains("every publication", geo3, StringComparison.Ordinal);
        Assert.Contains("every rollback", geo3, StringComparison.Ordinal);
        Assert.Contains("every merge", geo3, StringComparison.Ordinal);
        Assert.Contains("every override", geo3, StringComparison.Ordinal);
        Assert.Contains("every rejection", geo3, StringComparison.Ordinal);

        Assert.Contains("Publication must fail closed for:", geo3, StringComparison.Ordinal);
        Assert.Contains("incomplete hierarchy", geo3, StringComparison.Ordinal);
        Assert.Contains("cycle in hierarchy", geo3, StringComparison.Ordinal);
        Assert.Contains("broken provider parent mapping", geo3, StringComparison.Ordinal);
        Assert.Contains("optimistic concurrency conflict", geo3, StringComparison.Ordinal);
        Assert.Contains("active publication lock conflict", geo3, StringComparison.Ordinal);
    }

    [Fact]
    public void Geo3ArtifactsAndContext_MustRecordGovernanceFreeze()
    {
        var geo3 = CertificationSource.ReadRepoFile("docs/architecture/GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md");

        Assert.Contains("Reviewed ONLY Geographic Governance: **Yes**", geo3, StringComparison.Ordinal);
        Assert.Contains("Runtime files modified: **No**", geo3, StringComparison.Ordinal);
        Assert.Contains("Backend files modified: **No runtime backend files modified**", geo3, StringComparison.Ordinal);
        Assert.Contains("Frontend files modified: **No**", geo3, StringComparison.Ordinal);
        Assert.Contains("Architecture files added: **Yes**", geo3, StringComparison.Ordinal);
        Assert.Contains("Regression tests added: **Yes, architecture regression only**", geo3, StringComparison.Ordinal);
        Assert.Contains("Runtime hardening required: **Not in this phase**", geo3, StringComparison.Ordinal);
        Assert.Contains("Recommend next phase: **GEO-4 - Geographic Catalog Implementation Plan**", geo3, StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("GEO-3", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
