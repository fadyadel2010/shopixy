namespace PointOfSale.Tests.Architecture;

using System;
using Xunit;

/// <summary>
/// PASS A4 — Affiliate Payout Recovery Authority Certification
/// Regression-freeze tests for payout reconciliation, recovery convergence, and transactional consistency.
/// DO NOT DELETE OR RELAX THESE TESTS — they are production financial authority guards.
/// </summary>
public sealed class AffiliatePayoutRecoveryCertificationTests
{
    [Fact]
    public void PayoutReconciliation_AutoReconcile()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        // Service must automatically reconcile open withdrawal requests when money leaves the wallet
        Assert.Contains("public async Task AutoReconcileWithdrawalRequestsAsync", service, StringComparison.Ordinal);

        // Row-level locking is required for both Npgsql (FOR UPDATE) and SQL Server (UPDLOCK) to prevent concurrent reconciliation
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutReconciliation_DecimalPrecisionTolerance()
    {
        var requestEntity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs");

        // Hardened decimal precision tolerance (0.0001) to prevent rounding drift from blocking request closure
        Assert.Contains("const decimal TOLERANCE = 0.0001m;", requestEntity, StringComparison.Ordinal);
        Assert.Contains("if (RemainingAmount <= TOLERANCE)", requestEntity, StringComparison.Ordinal);
        Assert.Contains("RemainingAmount = 0m;", requestEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializableIsolation_PostgresRetryLoop()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Detect PostgreSQL error code 40001 serialization_failure
        Assert.Contains("IsSerializationFailure", orchestrator, StringComparison.Ordinal);
        Assert.Contains("pg.SqlState == \"40001\"", orchestrator, StringComparison.Ordinal);
        
        // Retry loop for SSI conflicts direct or wrapped in DbUpdateException
        Assert.Contains("ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException innerPg && innerPg.SqlState == \"40001\"", orchestrator, StringComparison.Ordinal);
        
        // Orchestrator retries max 3 times
        Assert.Contains("MAX_RETRIES = 3;", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboxReadiness_AccountingAndEventGates()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        // Payout must halt if critical accounting outbox is failed or stale
        Assert.Contains("EnsureAffiliateOutwardMoneyMovementReadyAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessage", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_ACCOUNTING_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);

        // Payout must halt if integration outbox events are stale or failed
        Assert.Contains("OutboxMessage", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVENT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);

        // MassTransit outbox backbone check is mandatory
        Assert.Contains("EnsureAffiliateMassTransitBackboneReadyAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("MassTransitOutboxMessage", orchestrator, StringComparison.Ordinal);
        Assert.Contains("OutboxState", orchestrator, StringComparison.Ordinal);
    }
}
