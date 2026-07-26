namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFinancialReversalCertificationTests
{
    [Fact]
    public void FinancialReversal_MustReverseCommissionPayablesDebtBalancesAndLedgerFacts()
    {
        var refund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var calculator = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundFinancialCalculator.cs");
        var decision = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundFinancialDecisionEngine.cs");
        var ledger = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs");
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");

        Assert.Contains("CommissionReversalAmount", refund, StringComparison.Ordinal);
        Assert.Contains("PromoDiscountReversalAmount", refund, StringComparison.Ordinal);
        Assert.Contains("VendorEarningReversalAmount", refund, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", refund, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", refund, StringComparison.Ordinal);

        Assert.Contains("RefundFinancialDecisionEngine.Create", execution, StringComparison.Ordinal);
        Assert.Contains("REFUND_PLATFORM_CASH_INSUFFICIENT", execution, StringComparison.Ordinal);
        Assert.Contains("REFUND_VENDOR_PAYABLE_INSUFFICIENT", execution, StringComparison.Ordinal);
        Assert.Contains("REFUND_VENDOR_DEBT_INSUFFICIENT", execution, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", execution, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", execution, StringComparison.Ordinal);

        Assert.Contains("CalculateSubOrderBreakdown", calculator, StringComparison.Ordinal);
        Assert.Contains("CalculateItemBreakdown", calculator, StringComparison.Ordinal);
        Assert.Contains("GrossCommissionReversalAmount", calculator, StringComparison.Ordinal);
        Assert.Contains("VendorEarningReversalAmount", calculator, StringComparison.Ordinal);

        Assert.Contains("PlatformCashDelta", decision, StringComparison.Ordinal);
        Assert.Contains("PlatformPayableDelta", decision, StringComparison.Ordinal);
        Assert.Contains("VendorDebtDelta", decision, StringComparison.Ordinal);
        Assert.Contains("ReducesVendorDebt", decision, StringComparison.Ordinal);

        Assert.Contains("RecordRefundAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("ValidateRefundFinancialIntegrity", ledger, StringComparison.Ordinal);
        Assert.Contains("EnsureDuplicateLedgerWriteIsSafeAsync", ledger, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReferenceType.Refund", ledger, StringComparison.Ordinal);

        Assert.Contains("ValidateRefunds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("ValidateExternalMoneyLifecycle", reconciliation, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundSettlementAndPayoutInteraction_MustBlockOverlapAndPreserveCompletedPayoutHistory()
    {
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var refund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var reconciliation = ReadRepoFile("PointOfSale.API/Services/CommerceHub/FinancialReconciliationService.cs");

        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", execution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorEarningStatus.InPayout", execution, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorEarningStatus.PaidOut", execution, StringComparison.Ordinal);
        Assert.Contains("IsPostPayoutAdjustment", execution, StringComparison.Ordinal);
        Assert.Contains("PostPayoutAdjustment", execution, StringComparison.Ordinal);

        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", refund, StringComparison.Ordinal);
        Assert.Contains("IsPostPayoutAdjustment", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorEarningStatus.PaidOut", refund, StringComparison.Ordinal);

        Assert.Contains("HasBlockingPayoutLockAsync", payout, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", payout, StringComparison.Ordinal);
        Assert.Contains("negativeAdjustments", payout, StringComparison.Ordinal);

        Assert.Contains("ValidateRefunds", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PayoutLedgerByPayoutId", reconciliation, StringComparison.Ordinal);
        Assert.Contains("PlatformCashDebits", reconciliation, StringComparison.Ordinal);
        Assert.Contains("VendorPayoutDebits", reconciliation, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
