namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS AF-PA5.5 - Commission Approval, Withdrawable Balance, and Financial Recovery freeze.
/// </summary>
public sealed class AffiliateCommissionApprovalWithdrawableBalanceCertificationTests
{
    [Fact]
    public void CommissionApproval_MustBeBackendDeliveryFinancialLifecycleOnly()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");
        var consumer = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateSettlementConsumers.cs");
        var approvalMethod = Slice(service, "public async Task ApproveCommissionForDeliveredOrderAsync", "private async Task<Order> LoadAffiliateApprovalOrderAuthorityAsync");
        var reconciliationMethod = Slice(service, "private async Task ReconcileDeliveredCommissionApprovalsAsync", "private async Task<bool> ShouldRetryMissingEarnedCommissionForRefundAsync");

        Assert.Contains("IConsumer<OrderDeliveredEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("IConsumer<FulfillmentDeliveredEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateDeliveryApprovalAuthority", approvalMethod, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", approvalMethod, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger.CreateApproved(earned)", approvalMethod, StringComparison.Ordinal);

        Assert.Contains("EnsureAffiliateDeliveryApprovalAuthority", reconciliationMethod, StringComparison.Ordinal);
        Assert.Contains("RecordCommissionDeliveredOrderReconciliation", reconciliationMethod, StringComparison.Ordinal);
        Assert.Contains("AffiliateCommissionLedger.CreateApproved(earned)", reconciliationMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void WithdrawableBalance_MustBeginAtNetApprovedLedgerOnly()
    {
        var payoutService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs");
        var balanceMethod = Slice(payoutService, "private async Task<AffiliateAvailableBalanceResult> GetAvailableBalanceCoreAsync", "private async Task<List<string>> ResolveAuthoritativeBalanceCurrenciesAsync");

        Assert.Contains("CommissionEntryType.Approved", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("CommissionEntryType.Reversed", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("var netApproved", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("Math.Max(item.ApprovedSum - item.ReversedSum, 0m)", balanceMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionEntryType.Earned", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("Available = NetApproved + PayoutReductions + Withdrawals (negative) - Pending", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("RawAvailable", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("AvailableForWithdrawal", balanceMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryAndReplay_MustBeAppendOnlyAndIdempotent()
    {
        var commissionLedger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateCommissionLedger.cs");
        var commissionConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateCommissionLedgerConfiguration.cs");
        var balanceLedger = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Affiliate/AffiliateBalanceLedger.cs");
        var balanceConfig = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/Affiliate/AffiliateBalanceLedgerConfiguration.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs");

        Assert.Contains("Append-only commission ledger entry", commissionLedger, StringComparison.Ordinal);
        Assert.Contains("APPEND ONLY", commissionLedger, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", commissionConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", commissionConfig, StringComparison.Ordinal);

        Assert.Contains("Account-level financial movements ledger", balanceLedger, StringComparison.Ordinal);
        Assert.Contains("APPEND ONLY", balanceLedger, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IdempotencyKey)", balanceConfig, StringComparison.Ordinal);
        Assert.Contains("IsUnique()", balanceConfig, StringComparison.Ordinal);

        Assert.Contains("IsUniqueViolation(ex)", service, StringComparison.Ordinal);
        Assert.Contains("RecordIdempotentSkip", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendAndCaches_MustNotOwnApprovalWithdrawableBalanceOrLedgerTotals()
    {
        var frontendFiles = Directory.EnumerateFiles(FindRepoRoot(), "*.*", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Web{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                           path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.Storefront{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                           path.Contains($"{Path.DirectorySeparatorChar}PointOfSale.CommerceHub{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var combinedFrontend = string.Join('\n', frontendFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("AffiliateCommissionLedger.CreateApproved", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateApproved(earned", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionEntryType.Approved", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("AffiliateBalanceLedger", combinedFrontend, StringComparison.Ordinal);
        Assert.DoesNotContain("RawAvailable =", combinedFrontend, StringComparison.Ordinal);
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
