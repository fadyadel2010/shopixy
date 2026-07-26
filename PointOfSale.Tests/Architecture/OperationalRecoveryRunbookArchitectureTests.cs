namespace PointOfSale.Tests.Architecture;

public sealed class OperationalRecoveryRunbookArchitectureTests
{
    [Fact]
    public void OperationalRecoveryRunbook_MustCoverRequiredRecoveryAreas()
    {
        var runbook = File.ReadAllText(RunbookPath());

        foreach (var requiredSection in new[]
        {
            "## Replay Recovery",
            "## Rebuild And Reindex Recovery",
            "## Queue Backlog And Throughput Recovery",
            "## Deployment And Restart Recovery",
            "## Backup, Restore, And DR Recovery",
            "## Cache And Projection Recovery",
            "## Tenant Isolation Incidents",
            "## Payment, Accounting, And Fulfillment Incidents",
            "## Operational Telemetry And Diagnostics",
            "## Dangerous Operational Primitive Rules",
            "## Remaining Debt"
        })
        {
            Assert.Contains(requiredSection, runbook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperationalRecoveryRunbook_MustPreserveSqlFirstReplayAndFailClosedSemantics()
    {
        var runbook = File.ReadAllText(RunbookPath());

        foreach (var marker in new[]
        {
            "PostgreSQL and the named authority services decide business truth",
            "Replay-safe means duplicate or stale delivery converges deterministically",
            "Fail-closed means the platform prefers hiding, deleting, tombstoning, blocking, or marking reconciliation-required",
            "Rebuild-safe means rebuilds recompose from current SQL",
            "Duplicate EventId delivery collapses before side effects",
            "Stale projections are deleted or tombstoned",
            "Replay telemetry is visible through `PointOfSale.Operational.Convergence`",
            "`payment_cancellation_outbox_readiness`",
            "`PointOfSale.Accounting`"
        })
        {
            Assert.Contains(marker, runbook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperationalRecoveryRunbook_MustProhibitDangerousOperatorShortcuts()
    {
        var runbook = File.ReadAllText(RunbookPath());
        var primitiveGovernance = File.ReadAllText(PrimitiveGovernancePath());

        foreach (var marker in new[]
        {
            "Manually editing Mongo, Redis, or Elasticsearch",
            "Full-replacing Mongo documents from old repair snapshots",
            "Manual Redis key mutation",
            "Raw Elasticsearch visibility writes",
            "Trusting `X-Tenant-Id`, query tenant ids, frontend tenant selectors, or repair payload tenant ids as authority",
            "Manually inserting ledger journal rows",
            "Disabling fences, freshness filters, or `DerivativeProjectionMutationGuard`"
        })
        {
            Assert.Contains(marker, runbook, StringComparison.Ordinal);
        }

        foreach (var marker in new[]
        {
            "Direct SQL or bulk mutation",
            "Full Mongo document replacement",
            "Raw Elasticsearch visibility write",
            "Redis pattern invalidation/manual key edit",
            "Raw MassTransit publish/send",
            "Manual financial repair",
            "Static approval is not enough for Tier 1 recovery paths"
        })
        {
            Assert.Contains(marker, primitiveGovernance, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperationalRecoveryRunbook_MustDocumentEscalationAndLightweightDebt()
    {
        var runbook = File.ReadAllText(RunbookPath());

        foreach (var marker in new[]
        {
            "Tenant-scoped operational work stays tenant-scoped",
            "All-tenant or global repair is platform scope only",
            "Payment, accounting, refund, payout, and fulfillment ambiguity escalates",
            "This runbook intentionally stays lightweight",
            "It does not define enterprise IAM",
            "It does not introduce adaptive throttling",
            "It does not claim fixed throughput or rebuild-duration SLOs"
        })
        {
            Assert.Contains(marker, runbook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperationalRecoveryRunbook_MustDocumentDeploymentRestartAndDrAssumptions()
    {
        var runbook = File.ReadAllText(RunbookPath());

        foreach (var marker in new[]
        {
            "Rolling restart is safe only because restarted workers re-enter the same SQL-first leases, fences, processed-event checks, and lifecycle boundaries",
            "Do not assume a clean shutdown or zero overlap",
            "Restore PostgreSQL first and treat it as the only business-truth restore boundary",
            "MongoDB, Redis, Elasticsearch, RabbitMQ, and dashboard snapshots as derivative or transport evidence only",
            "No repo-authoritative orchestration currently guarantees point-in-time cross-store restore consistency",
            "No certified per-tenant restore workflow exists"
        })
        {
            Assert.Contains(marker, runbook, StringComparison.Ordinal);
        }
    }

    private static string RunbookPath()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "docs", "operations", "operational-recovery-runbooks.md");
    }

    private static string PrimitiveGovernancePath()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "docs", "architecture", "dangerous-operational-primitives.md");
    }

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
