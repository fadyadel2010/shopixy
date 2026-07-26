namespace PointOfSale.Tests.Architecture;

public sealed class WarehouseOperationalReadinessCertificationTests
{
    [Fact]
    public void Warehouse4CertificationDocument_MustRecordOperationalEvidenceDecision()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_4_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("Status: NOT OPERATIONALLY CERTIFIED - EVIDENCE REQUIRED", certification, StringComparison.Ordinal);
        Assert.Contains("Monitoring Review", certification, StringComparison.Ordinal);
        Assert.Contains("Observability Review", certification, StringComparison.Ordinal);
        Assert.Contains("Operational Drills", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse Health", certification, StringComparison.Ordinal);
        Assert.Contains("Security Operations", certification, StringComparison.Ordinal);
        Assert.Contains("Browser/Device Evidence", certification, StringComparison.Ordinal);
        Assert.Contains("Support Readiness", certification, StringComparison.Ordinal);
        Assert.Contains("Rollout Readiness", certification, StringComparison.Ordinal);
        Assert.Contains("Operational Blockers", certification, StringComparison.Ordinal);
        Assert.Contains("Certification Decision", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse4MonitoringEvidence_MustUseExistingSourceEvidenceWithoutClaimingDeploymentProof()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_4_OPERATIONAL_READINESS_CERTIFICATION.md");
        var telemetry = ReadRepoFile("PointOfSale.Infrastructure/Instrumentation/InventoryReservationTelemetry.cs");
        var metrics = ReadRepoFile("PointOfSale.Infrastructure/Observability/InventoryReservationMetricsService.cs");
        var healthChecks = ReadRepoFile("PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs");

        Assert.Contains("inventory.reservations.total_reserved_quantity", telemetry, StringComparison.Ordinal);
        Assert.Contains("inventory.reservations.stale_count", telemetry, StringComparison.Ordinal);
        Assert.Contains("InventoryReservationMetricsSnapshot", metrics, StringComparison.Ordinal);
        Assert.Contains("StockReservationHealthCheck", healthChecks, StringComparison.Ordinal);
        Assert.Contains("ProjectionOutboxHealthCheck", healthChecks, StringComparison.Ordinal);

        Assert.Contains("No deployed Warehouse dashboard evidence.", certification, StringComparison.Ordinal);
        Assert.Contains("No alert-routing signoff", certification, StringComparison.Ordinal);
        Assert.Contains("Monitoring decision: **Not certified.**", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse4ObservabilityAndSecurity_MustPreserveSourceLineageButRequireOperationalProof()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_4_OPERATIONAL_READINESS_CERTIFICATION.md");
        var warehouse1 = ReadRepoFile("docs/architecture/WAREHOUSE_1_RUNTIME_AUTHORITY.md");
        var warehouse2 = ReadRepoFile("docs/architecture/WAREHOUSE_2_ADMINISTRATION_STOREFRONT_APIS.md");
        var runbook = ReadRepoFile("docs/operations/operational-recovery-runbooks.md");

        Assert.Contains("Reservation audit is represented by inventory event inbox rows", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Transfer audit includes transfer status", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Return audit is append-only", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Tenant isolation uses trusted `ITenantContext.RequiredTenantId`", warehouse2, StringComparison.Ordinal);
        Assert.Contains("Inventory / Reservation / Warehouse Authority Incidents", runbook, StringComparison.Ordinal);

        Assert.Contains("No deployed distributed tracing proof", certification, StringComparison.Ordinal);
        Assert.Contains("No production access-control review signoff", certification, StringComparison.Ordinal);
        Assert.Contains("Observability decision: **Not certified.**", certification, StringComparison.Ordinal);
        Assert.Contains("Security Operations decision: **Not certified.**", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse4OperationalReadiness_MustNotModifyRuntimeBackendFrontendOrAiContext()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_4_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("Runtime modified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Backend modified? **No production backend code modified.**", certification, StringComparison.Ordinal);
        Assert.Contains("Frontend modified? **No production frontend code modified.**", certification, StringComparison.Ordinal);
        Assert.Contains("No new permanent operational architecture truth was discovered, so `.ai-context` files were not modified.", certification, StringComparison.Ordinal);
        Assert.Contains("Remaining Development Blockers? **None found in this operational-readiness review.**", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse4SuccessAnswers_MustRemainEvidenceBased()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_4_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("Reviewed ONLY Warehouse Operational Readiness? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("Operational Readiness Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse Monitoring Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Observability Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Receiving Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Transfer Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Returns Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Reservation Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse Health Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Browser Compatibility Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Mobile Compatibility Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Accessibility Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Security Operationally Certified? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Development Complete? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("Operational Ready? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Production Ready? **No.**", certification, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
