namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS AF-PA6.4 - Financial Recovery, Replay, Idempotency, and Ledger Integrity freeze.
/// </summary>
public sealed class AffiliateFinancialRecoveryReplayLedgerIntegrityCertificationTests
{
    [Fact]
    public void RecoveryAuthority_MustBeginAtBackendOrchestratorAndCurrentSql()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminAffiliateController.cs");
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");

        Assert.Contains("_orchestrator.ExecuteAffiliatePayoutAsync", controller, StringComparison.Ordinal);
        Assert.Contains("if (request.Status != PayoutRequestStatus.Approved)", controller, StringComparison.Ordinal);
        Assert.Contains("TryGetCurrentAdminUserId(out var adminUserId)", controller, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", orchestrator, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateOutwardMoneyMovementReadyAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("GetAvailableBalanceAsync(affiliateAccountId, storeTenantId", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AcquireAffiliatePayoutBalanceLockAsync", payoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_MustNotDuplicateWithdrawalSettlementOrExternalTransferAllocation()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");
        var balanceLedger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateBalanceLedger.cs");
        var allocation = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliatePayoutTransferAllocation.cs");
        var allocationConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePayoutTransferAllocationConfiguration.cs");
        var evidenceConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliatePayoutTransferEvidenceConfiguration.cs");

        Assert.Contains("existingWithdrawal", orchestrator, StringComparison.Ordinal);
        Assert.Contains("WasIdempotentSkip = true", orchestrator, StringComparison.Ordinal);
        Assert.Contains("Idempotency conflict", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutTransferAllocation.BuildIdempotencyKey", orchestrator, StringComparison.Ordinal);
        Assert.Contains("existingAllocation", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_IDEMPOTENCY_CONFLICT", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_BALANCE:WITHDRAWAL:{payoutRequestId}", balanceLedger, StringComparison.Ordinal);
        Assert.Contains("BuildIdempotencyKey", allocation, StringComparison.Ordinal);
        Assert.Contains("HasIndex(e => e.IdempotencyKey)", allocationConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", allocationConfig, StringComparison.Ordinal);
        Assert.Contains("HasIndex(e => new { e.RailNamespace, e.NormalizedPaymentReference })", evidenceConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", evidenceConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerTruth_MustBeAppendOnlyAndPersistenceGuarded()
    {
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var commissionConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");
        var balanceConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateBalanceLedgerConfiguration.cs");
        var commissionLedger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs");
        var balanceLedger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateBalanceLedger.cs");

        Assert.Contains("GuardAffiliatePayoutAuthority();", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<AffiliateCommissionLedger>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("nameof(AffiliateCommissionLedger)", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be changed", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<AffiliateBalanceLedger>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("nameof(AffiliateBalanceLedger)", dbContext, StringComparison.Ordinal);

        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", commissionConfig, StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliateCommLedger_Idempotency", commissionConfig, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", balanceConfig, StringComparison.Ordinal);
        Assert.Contains("UQ_AffiliateBalanceLedger_Idempotency", balanceConfig, StringComparison.Ordinal);
        Assert.Contains("APPEND ONLY", commissionLedger, StringComparison.Ordinal);
        Assert.Contains("APPEND ONLY", balanceLedger, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceRebuild_MustConvergeFromLedgerTruthOnly()
    {
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var balanceMethod = Slice(payoutService, "private async Task<AffiliateAvailableBalanceResult> GetAvailableBalanceCoreAsync", "private async Task<List<string>> ResolveAuthoritativeBalanceCurrenciesAsync");

        Assert.Contains("CommissionEntryType.Approved", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Reversed", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("BalanceEntryType.Withdrawal", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("l.EntryType != BalanceEntryType.Withdrawal", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("l.Amount < 0m", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("PayoutRequestStatus.Pending", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("PayoutRequestStatus.Approved", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("Available = NetApproved + PayoutReductions + Withdrawals (negative) - Pending", balanceMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionEntryType.Earned", balanceMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboxRecovery_MustBlockMoneyMovementWhenFinancialBackboneIsUnresolved()
    {
        var orchestrator = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs");

        Assert.Contains("AFFILIATE_PAYOUT_ACCOUNTING_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_EVENT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MASS_TRANSIT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_MARKETPLACE_EVENT_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_STOCK_SYNC_BACKBONE_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_CAPTURED_PAYMENT_NOT_READY", orchestrator, StringComparison.Ordinal);
        Assert.Contains("AFFILIATE_PAYOUT_UNRESOLVED_PROVIDER_REFUND_NOT_READY", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_MustNotOwnRecoveryReplayLedgerSettlementOrBalanceTruth()
    {
        var frontendFiles = Directory.EnumerateFiles(FindRepoRoot(), "*.*", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Web{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var combinedFrontend = string.Join('\n', frontendFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("ExecuteAffiliatePayoutAsync", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateBalanceLedger", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateCommissionLedger", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliatePayoutTransferAllocation", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("RawAvailable =", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("BalanceEntryType.Withdrawal", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionEntryType.Paid", combinedFrontend, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
