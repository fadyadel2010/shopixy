namespace PointOfSale.Tests.Architecture;

public sealed class OperationalSurvivabilityCertificationTests
{
    [Fact]
    public void OpenTelemetryPipeline_MustRegisterReviewedOperationalMeters_AndValidateReindexOptions()
    {
        var program = File.ReadAllText(ApiPath("Program.cs"));
        var observability = File.ReadAllText(InfrastructurePath("Extensions", "ObservabilityServiceExtensions.cs"));

        foreach (var marker in new[]
        {
            "AddMeter(PointOfSale.Infrastructure.Instrumentation.MarketplaceTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Instrumentation.AffiliateTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Instrumentation.InventoryReservationTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Accounting.AccountingMetrics.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Redis.Services.RedisPerformanceTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Elasticsearch.Services.SearchTelemetry.MeterName)",
            "AddMeter(PointOfSale.API.Services.Checkout.CheckoutLockTelemetry.MeterName)",
            "AddMeter(PointOfSale.Infrastructure.Instrumentation.OperationalConvergenceTelemetry.MeterName)",
            "AddOptions<PointOfSale.Infrastructure.Jobs.CommerceHubReindexOptions>()",
            "CommerceHubReindex:ParallelBatchSize must be between 1 and 32.",
            "CommerceHubReindex:SearchSyncStatusBatchSize must be between 1 and 1000.",
            "CommerceHubReindex:InterBatchDelayMilliseconds must be between 0 and 60000.",
            ".ValidateOnStart();"
        })
        {
            Assert.Contains(marker, program, StringComparison.Ordinal);
        }

        Assert.Contains("payment_cancellation_outbox_readiness", observability, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass8Documentation_MustRecordOperationalSurvivabilityFindings_AndFreezeInvariants()
    {
        var findings = File.ReadAllText(DocsPath("architecture", "operational-survivability-certification-findings.md"));
        var maturity = File.ReadAllText(DocsPath("architecture", "operational-maturity-scalability-findings.md"));
        var runbook = File.ReadAllText(DocsPath("operations", "operational-recovery-runbooks.md"));
        var primitives = File.ReadAllText(DocsPath("architecture", "dangerous-operational-primitives.md"));
        var projectContext = File.ReadAllText(ContextPath("PROJECT_CONTEXT.md"));
        var architectureTruth = File.ReadAllText(ContextPath("ARCHITECTURE_TRUTH.md"));
        var knownRisks = File.ReadAllText(ContextPath("KNOWN_RISKS.md"));
        var rules = File.ReadAllText(ContextPath("CODEX_RULES.md"));

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Certification",
            "Telemetry & Observability Certification",
            "Throughput & Scaling Survivability Certification",
            "Deployment / Restart Safety Certification",
            "Disaster Recovery Assumption Certification",
            "Auditability & Governance Survivability Certification",
            "OpenTelemetry",
            "tenant_bucket",
            "PointOfSale.Accounting",
            "CommerceHubReindexOptions",
            "Redis cache telemetry",
            "payment cancellation",
            "rolling restart",
            "PostgreSQL is the only reviewed restore authority",
            "Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("tenant_bucket", maturity, StringComparison.Ordinal);
        Assert.Contains("OpenTelemetry meter registration", maturity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Deployment And Restart Recovery", runbook, StringComparison.Ordinal);
        Assert.Contains("## Backup, Restore, And DR Recovery", runbook, StringComparison.Ordinal);
        Assert.Contains("tenant_bucket", runbook, StringComparison.Ordinal);
        Assert.Contains("PASS 8 Operational Survivability Freeze", primitives, StringComparison.Ordinal);
        Assert.Contains("PASS 8 Operational Survivability Certification", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 8 Operational Survivability Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Operational telemetry export and cardinality are now governed", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 8 Operational Survivability Freeze", rules, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL as the only reviewed restore authority", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportedCommerceReliabilityTelemetry_MustNotEmitRawTenantOrOrderDimensions()
    {
        var source = File.ReadAllText(Path.Combine(
            DiscoverSolutionRoot(AppContext.BaseDirectory),
            "PointOfSale.Infrastructure",
            "Instrumentation",
            "CommerceReliabilityTelemetry.cs"));

        Assert.DoesNotContain("\"tenant_id\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"order_id\"", source, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalTenantBucketTag(tenantId)", source, StringComparison.Ordinal);
        Assert.Contains("\"order_present\"", source, StringComparison.Ordinal);
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
