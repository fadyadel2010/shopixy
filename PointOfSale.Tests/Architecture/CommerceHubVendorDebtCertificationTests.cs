namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubVendorDebtCertificationTests
{
    [Fact]
    public void VendorDebt_MustBeSqlLedgerDerivedWithoutShadowBalanceAuthority()
    {
        var debt = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorDebtService.cs");
        var ledger = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs");
        var dashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");

        Assert.Contains("CommerceHubFinancialTransactions", debt, StringComparison.Ordinal);
        Assert.Contains("TransactionType == CommerceHubFinancialTransactionType.Commission", debt, StringComparison.Ordinal);
        Assert.Contains("TransactionType == CommerceHubFinancialTransactionType.Adjustment", debt, StringComparison.Ordinal);
        Assert.Contains("TransactionType == CommerceHubFinancialTransactionType.Settlement", debt, StringComparison.Ordinal);
        Assert.Contains("postPayoutRefundRows", debt, StringComparison.Ordinal);
        Assert.Contains("legacyCommissionRows", debt, StringComparison.Ordinal);
        Assert.Contains("directCommissionReferenceKeys", debt, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0m, runningDebt)", debt, StringComparison.Ordinal);
        Assert.Contains("FinancialBucket", ledger, StringComparison.Ordinal);
        Assert.Contains("BuildVendorControlAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("GetVendorDebtSnapshotAsync", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorDebtThreshold_MustUseHardLimitForSuspensionAndCrossingEvidence()
    {
        var options = ReadRepoFile("PointOfSale.API/Configuration/CommerceHubVendorDebtOptions.cs");
        var debt = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorDebtService.cs");

        Assert.Contains("DefaultSuspensionThresholdAmount = 1000m", options, StringComparison.Ordinal);
        Assert.Contains("var isSuspended = amountOwed >= threshold;", debt, StringComparison.Ordinal);
        Assert.Contains("if (effectiveDebt < threshold)", debt, StringComparison.Ordinal);
        Assert.Contains("previousDebt < threshold && effectiveDebt >= threshold", debt, StringComparison.Ordinal);
        Assert.Contains("amountOwed >= threshold", debt, StringComparison.Ordinal);
        Assert.Contains("amountOwed - threshold + 0.01m", debt, StringComparison.Ordinal);
    }

    [Fact]
    public void DebtReduction_MustRequireEvidenceIdempotencyLedgerPostingAndDeltaVerification()
    {
        var control = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs");
        var payments = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs");

        Assert.Contains("RecordSettlementPaymentAsync", control, StringComparison.Ordinal);
        Assert.Contains("ClearVendorDebtAsync", control, StringComparison.Ordinal);
        Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", control, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_REFERENCE_REQUIRED", control, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_REFERENCE_ALREADY_USED", control, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_AMOUNT_EXCEEDS_DEBT", control, StringComparison.Ordinal);
        Assert.Contains("DEBT_CLEARANCE_EXCEEDS_DEBT", control, StringComparison.Ordinal);
        Assert.Contains("RecordMirroredTransactionsAsync", control, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", control, StringComparison.Ordinal);
        Assert.Contains("AppendCommittedOperationAudit", control, StringComparison.Ordinal);

        Assert.Contains("ApprovePaymentRequestAsync", payments, StringComparison.Ordinal);
        Assert.Contains("EnsureApprovalAmountIsValid", payments, StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync", payments, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorPaymentAuditLog.Create", payments, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
