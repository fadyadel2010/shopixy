namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryReservationAuthorityCertificationTests
{
    [Fact]
    public void CommerceHubPayoutReservations_MustDeriveFromSqlPayoutAuthority()
    {
        var solution = GetSolutionDirectory();
        var payout = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPayoutService.cs"));
        var dbContext = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs"));

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", payout, StringComparison.Ordinal);
        Assert.Contains("LockVendorEarningsForPayoutCreationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("earning.PayoutReservedAmount = RoundCurrency(earning.PayoutReservedAmount + allocation.Amount)", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Earning", dbContext, StringComparison.Ordinal);
        Assert.Contains("payout reservation amount can only be changed by the CommerceHub payout boundary", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundTriggeredPayoutRelease_MustReleaseReservedAmountAndBlockProcessingPayouts()
    {
        var solution = GetSolutionDirectory();
        var refund = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubRefundService.cs"));
        var execution = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "RefundExecutionService.cs"));
        var compensation = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPaymentCompensationOutboxWorker.cs"));

        AssertReleasePathIsHardened(refund);
        AssertReleasePathIsHardened(execution);
        AssertReleasePathIsHardened(compensation);
    }

    [Fact]
    public void PayoutExecutionAndFailure_MustConsumeOrReleaseExistingSqlReservation()
    {
        var solution = GetSolutionDirectory();
        var payout = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "CommerceHubPayoutService.cs"));

        Assert.Contains("item.Earning.Status != CommerceHubVendorEarningStatus.InPayout || item.Earning.PayoutId != payout.Id", payout, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation(item.Earning, item.Amount", payout, StringComparison.Ordinal);
        Assert.Contains("ReleasePayoutAllocation(item.Earning, item.Amount)", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EARNING_STATE_INVALID", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED", payout, StringComparison.Ordinal);
    }

    [Fact]
    public void AffiliatePayoutReservations_MustRemainSqlLedgerDerived()
    {
        var solution = GetSolutionDirectory();
        var affiliatePayout = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Services",
            "Affiliate",
            "AffiliatePayoutService.cs"));
        var settlement = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Services",
            "Affiliate",
            "AffiliateSettlementOrchestrator.cs"));
        var dbContext = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs"));

        Assert.Contains("totalPendingRequests", affiliatePayout, StringComparison.Ordinal);
        Assert.Contains("Status == PayoutRequestStatus.Pending", affiliatePayout, StringComparison.Ordinal);
        Assert.Contains("Status == PayoutRequestStatus.Approved", affiliatePayout, StringComparison.Ordinal);
        Assert.Contains("AcquireAffiliatePayoutBalanceLockAsync", affiliatePayout, StringComparison.Ordinal);
        Assert.Contains("ExecuteAffiliatePayoutAsync", settlement, StringComparison.Ordinal);
        Assert.Contains("AffiliatePayoutRequest)} identity, amount, currency, tenant, affiliate, and payout destination snapshot are immutable", dbContext, StringComparison.Ordinal);
    }

    private static void AssertReleasePathIsHardened(string source)
    {
        Assert.Contains("REFUND_BLOCKED_BY_PROCESSING_PAYOUT", source, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", source, StringComparison.Ordinal);
        Assert.Contains("payoutItem.Earning.PayoutReservedAmount = RoundCurrency(Math.Max(0m, payoutItem.Earning.PayoutReservedAmount - allocation))", source, StringComparison.Ordinal);
        Assert.Contains("payoutItem.Earning.Status = CommerceHubVendorEarningStatus.Available", source, StringComparison.Ordinal);
        Assert.Contains("payoutItem.Earning.PayoutId = null", source, StringComparison.Ordinal);
    }

    private static string GetSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate solution directory.");
    }
}
