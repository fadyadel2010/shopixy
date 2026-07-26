namespace PointOfSale.Tests.Architecture;

public sealed class CustomsAdministrationAdmissionAuthorityTests
{
    [Fact]
    public void Customs3Document_MustExistAndRemainGovernanceOnly()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        Assert.Contains("# CUSTOMS-3 - Customs Administration and Admission Authority", customs3, StringComparison.Ordinal);
        Assert.Contains("Status: Governance authority freeze", customs3, StringComparison.Ordinal);
        Assert.Contains("Scope: Customs Administration Authority and Admission Governance only", customs3, StringComparison.Ordinal);
        Assert.Contains("Runtime impact: none", customs3, StringComparison.Ordinal);
        Assert.Contains("Backend runtime impact: none", customs3, StringComparison.Ordinal);
        Assert.Contains("Frontend impact: none", customs3, StringComparison.Ordinal);
        Assert.Contains("Product UI impact: none", customs3, StringComparison.Ordinal);
        Assert.Contains("Carrier adapter impact: none", customs3, StringComparison.Ordinal);
        Assert.Contains("International shipping implementation impact: none", customs3, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorityMatrix_MustAnswerRequiredAdministrationQuestions()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        foreach (var requiredQuestion in new[]
                 {
                     "Who may create Customs Evidence?",
                     "Who may edit Customs Evidence?",
                     "Who may approve Customs Evidence?",
                     "Who may publish Customs Evidence?",
                     "Who may retire Customs Evidence?",
                     "Who may correct Customs Evidence?",
                     "Who may reopen rejected evidence?",
                     "Who may use draft evidence?"
                 })
        {
            Assert.Contains(requiredQuestion, customs3, StringComparison.Ordinal);
        }

        Assert.Contains("Role separation is mandatory for production publication.", customs3, StringComparison.Ordinal);
        Assert.Contains("Product remains source evidence only.", customs3, StringComparison.Ordinal);
        Assert.Contains("Customs owns admitted evidence.", customs3, StringComparison.Ordinal);
        Assert.Contains("Shipment owns the immutable shipment customs snapshot.", customs3, StringComparison.Ordinal);
        Assert.Contains("Carrier adapters consume only the admitted snapshot-derived DTO.", customs3, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceLifecycle_MustFreezeValidAndInvalidTransitions()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        foreach (var lifecycleState in new[]
                 {
                     "Draft",
                     "Under Review",
                     "Approved",
                     "Published",
                     "Retired",
                     "Superseded",
                     "Rejected",
                     "Reopened"
                 })
        {
            Assert.Contains(lifecycleState, customs3, StringComparison.Ordinal);
        }

        Assert.Contains("| Draft | Under Review |", customs3, StringComparison.Ordinal);
        Assert.Contains("| Under Review | Approved |", customs3, StringComparison.Ordinal);
        Assert.Contains("| Approved | Published |", customs3, StringComparison.Ordinal);
        Assert.Contains("| Published | Retired |", customs3, StringComparison.Ordinal);
        Assert.Contains("| Published | Superseded |", customs3, StringComparison.Ordinal);
        Assert.Contains("`Draft` -> `Published`", customs3, StringComparison.Ordinal);
        Assert.Contains("`Approved` mutation in place", customs3, StringComparison.Ordinal);
        Assert.Contains("publication without approval", customs3, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalAndPublication_MustRequireSeparationAndCurrentValidation()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        Assert.Contains("Create Draft", customs3, StringComparison.Ordinal);
        Assert.Contains("-> Validate Evidence", customs3, StringComparison.Ordinal);
        Assert.Contains("-> Submit Under Review", customs3, StringComparison.Ordinal);
        Assert.Contains("-> Approve or Reject", customs3, StringComparison.Ordinal);
        Assert.Contains("-> Publish Approved Version", customs3, StringComparison.Ordinal);
        Assert.Contains("-> Admit Published Version into Shipment Snapshot", customs3, StringComparison.Ordinal);
        Assert.Contains("Approval binds to the exact evidence version and validation checksum.", customs3, StringComparison.Ordinal);
        Assert.Contains("Any material edit invalidates prior approval", customs3, StringComparison.Ordinal);
        Assert.Contains("Approval does not make evidence shipment-usable until publication succeeds.", customs3, StringComparison.Ordinal);
    }

    [Fact]
    public void VersioningAuditAndAdmission_MustRemainImmutableAndAppendOnly()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        Assert.Contains("Approved evidence cannot mutate.", customs3, StringComparison.Ordinal);
        Assert.Contains("Corrections create a new evidence version.", customs3, StringComparison.Ordinal);
        Assert.Contains("Historical shipments continue to reference the evidence version frozen", customs3, StringComparison.Ordinal);
        Assert.Contains("Evidence retirement blocks future admissions but never rewrites history.", customs3, StringComparison.Ordinal);
        Assert.Contains("Customs audit is append-only and mandatory.", customs3, StringComparison.Ordinal);

        foreach (var auditEvent in new[]
                 {
                     "Creation",
                     "Review",
                     "Approval",
                     "Publication",
                     "Correction",
                     "Retirement",
                     "Rejection",
                     "Version creation",
                     "Snapshot admission"
                 })
        {
            Assert.Contains(auditEvent, customs3, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FailClosedRules_MustRejectNonPublishedOrMismatchedEvidence()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        foreach (var failure in new[]
                 {
                     "Draft evidence in shipment",
                     "Rejected evidence in shipment",
                     "Retired evidence in shipment",
                     "Wrong tenant",
                     "Wrong product",
                     "Version mismatch",
                     "Missing approval",
                     "Missing publication",
                     "Evidence status is not `Published`"
                 })
        {
            Assert.Contains(failure, customs3, StringComparison.Ordinal);
        }

        Assert.Contains("no provider call", customs3, StringComparison.Ordinal);
        Assert.Contains("no label request", customs3, StringComparison.Ordinal);
        Assert.Contains("no successful carrier", customs3, StringComparison.Ordinal);
        Assert.Contains("no carrier-neutral DTO emission", customs3, StringComparison.Ordinal);
    }

    [Fact]
    public void Customs3ArtifactsAndContext_MustRecordGovernanceFreeze()
    {
        var customs3 = CertificationSource.ReadRepoFile("docs/architecture/CUSTOMS-3_CUSTOMS_ADMINISTRATION_AND_ADMISSION_AUTHORITY.md");

        Assert.Contains("Reviewed ONLY Customs Administration? **Yes.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Runtime modified? **No.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Backend modified? **No runtime backend files modified; architecture tests added.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Frontend modified? **No.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Governance implemented? **Yes, as architecture/governance certification.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Approval workflow defined? **Yes.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Evidence lifecycle frozen? **Yes.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Versioning governed? **Yes.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Audit governed? **Yes.**", customs3, StringComparison.Ordinal);
        Assert.Contains("Recommend next phase: **Customs Administration Runtime Workflow Implementation.**", customs3, StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     ".ai-context/PROJECT_CONTEXT.md",
                     ".ai-context/ARCHITECTURE_TRUTH.md",
                     ".ai-context/KNOWN_RISKS.md",
                     ".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md",
                     ".ai-context/CODEX_RULES.md"
                 })
        {
            Assert.Contains("CUSTOMS-3", CertificationSource.ReadRepoFile(path), StringComparison.Ordinal);
        }
    }
}
