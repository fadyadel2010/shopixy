namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubSettlementCertificationTests
{
    [Fact]
    public void SettlementRecords_MustBeSqlLedgerMirroredIdempotentAndEvidenceBacked()
    {
        var settlement = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorSettlement.cs");
        var control = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs");
        var safety = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialSafetyGuardService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CommerceHubVendorSettlement", settlement, StringComparison.Ordinal);
        Assert.Contains("VendorId", settlement, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", settlement, StringComparison.Ordinal);
        Assert.Contains("ExternalReference", settlement, StringComparison.Ordinal);
        Assert.Contains("rejectLegacyPrefix: true", settlement, StringComparison.Ordinal);

        Assert.Contains("RecordSettlementPaymentAsync", control, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorSettlement.Create", control, StringComparison.Ordinal);
        Assert.Contains("EnsureSettlementReplayMatchesOriginal", control, StringComparison.Ordinal);
        Assert.Contains("EXTERNAL_REFERENCE_ALREADY_USED", control, StringComparison.Ordinal);
        Assert.Contains("RecordMirroredTransactionsAsync", control, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", control, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", control, StringComparison.Ordinal);
        Assert.Contains("AppendCommittedOperationAudit", control, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_AMOUNT_EXCEEDS_DEBT", control, StringComparison.Ordinal);

        Assert.Contains("CommerceHubFinancialStateSnapshot", safety, StringComparison.Ordinal);
        Assert.Contains("EnsureNonNegative", safety, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedDelta", safety, StringComparison.Ordinal);
        Assert.Contains("EnsureCorrelationIntegrityAsync", safety, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorSettlements", dbContext, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialOperationAudits", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void SettlementPayouts_MustRequireLedgerConfirmedBalancesAndExternalHandoffEvidence()
    {
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var payoutEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubPayout.cs");
        var readService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPayoutReadService.cs");

        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payout, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffToken", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", payout, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", payout, StringComparison.Ordinal);

        Assert.Contains("ExternalReferenceId", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("BankTransactionId", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("CompletionEvidenceFingerprint", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("PayoutBlockedByAuthorityConflict", readService, StringComparison.Ordinal);
        Assert.Contains("EligibleAmount", readService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
