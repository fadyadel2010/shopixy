namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubPayoutCertificationTests
{
    [Fact]
    public void MerchantPayoutLifecycle_MustStayLedgerConfirmedExternallyFencedAndReconciliationSafe()
    {
        var payout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPayoutService.cs");
        var payoutEntity = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubPayout.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPayout()", payout, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", payout, StringComparison.Ordinal);
        Assert.Contains("FindPayoutByIdempotencyAsync", payout, StringComparison.Ordinal);
        Assert.Contains("LockVendorEarningsForPayoutCreationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_INVALID", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_CURRENCY_MISMATCH", payout, StringComparison.Ordinal);
        Assert.Contains("negativeAdjustments", payout, StringComparison.Ordinal);
        Assert.Contains("PayoutReservedAmount", payout, StringComparison.Ordinal);

        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", payout, StringComparison.Ordinal);
        Assert.Contains("HasBlockingPayoutLockAsync", payout, StringComparison.Ordinal);
        Assert.Contains("CaptureAndValidatePayoutFinancialReadinessAsync", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferIdempotencyKey", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffToken", payout, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferIntentMatchesPayout", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalTransferHandoffMatchesPayout", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureRequestedPayoutEvidenceMatchesStored", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", payout, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureTerminalPayoutEvidence", payout, StringComparison.Ordinal);
        Assert.Contains("EnsureCompletionEvidenceFingerprint", payout, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", payout, StringComparison.Ordinal);
        Assert.Contains("ApplyCompletedPayoutAllocation", payout, StringComparison.Ordinal);
        Assert.Contains("confirmed as not executed", payout, StringComparison.Ordinal);
        Assert.Contains("CommerceHubPayoutStatus.ReconciliationRequired", payout, StringComparison.Ordinal);

        Assert.Contains("CompletionEvidenceFingerprint", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("ExternalTransferHandoffCommittedAtUtc", payoutEntity, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_OneProcessingPerTenant", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_ExternalReferenceId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_Live_BankTransactionId", dbContext, StringComparison.Ordinal);
        Assert.Contains("UX_CommerceHubPayouts_ExternalTransferIdempotencyKey", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Completed_Evidence", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_CommerceHubPayouts_Processing_HandoffCommitted", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutExecution_MustBindVendorMethodExternalEvidenceActorAndLedgerState()
    {
        var service = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");
        var controller = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        var frontend = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");
        var tests = ReadRepoFile("PointOfSale.Tests/CommerceHub/SuperAdminPayoutsServiceTests.cs");

        Assert.Contains("SuperAdminBaseController", controller, StringComparison.Ordinal);
        Assert.Contains("CurrentSuperAdminId", controller, StringComparison.Ordinal);
        Assert.Contains("VENDOR_PAYOUT_EXECUTED", controller, StringComparison.Ordinal);
        Assert.Contains("VENDOR_PAYOUT_QUEUE_VIEWED", controller, StringComparison.Ordinal);

        Assert.Contains("NormalizeActorId", service, StringComparison.Ordinal);
        Assert.Contains("SUPER_ADMIN_ACTOR_REQUIRED", service, StringComparison.Ordinal);
        Assert.Contains("method.Id == request.PayoutMethodId", service, StringComparison.Ordinal);
        Assert.Contains("method.VendorId == vendorId", service, StringComparison.Ordinal);
        Assert.Contains("EnsurePayoutCompletionEvidencePresent", service, StringComparison.Ordinal);
        Assert.Contains("A completed Super Admin vendor payout requires an external reference or bank transaction id.", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_EXCEEDS_PAYABLE", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_AMOUNT_EXCEEDS_ELIGIBLE", service, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_PLATFORM_CASH_INSUFFICIENT", service, StringComparison.Ordinal);
        Assert.Contains("CaptureStateForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("VerifyStateTransitionAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureExternalPayoutEvidenceIsUniqueAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildCompletionEvidenceFingerprint", service, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", service, StringComparison.Ordinal);
        Assert.Contains("AppendCommittedOperationAudit", service, StringComparison.Ordinal);
        Assert.Contains("CanRecordCompletedPayout", service, StringComparison.Ordinal);
        Assert.Contains("ExecutionBlockedCode", service, StringComparison.Ordinal);

        Assert.Contains("Use this only after the external transfer has already been executed", frontend, StringComparison.Ordinal);
        Assert.Contains("Immutable provider or bank evidence is required", frontend, StringComparison.Ordinal);

        Assert.Contains("ExecuteAsync_ShouldRejectExternalEvidenceAlreadyUsedByAnotherVendor", tests, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync_ShouldRejectAllocatedEarningsWithoutSubOrderLedgerConfirmation", tests, StringComparison.Ordinal);
        Assert.Contains("GetQueueAsync_ShouldExposeBackendExecutionBlockWhenPriorPayoutNeedsReconciliation", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void PayoutHttpMutation_MustRequireSuperAdminExecutionWorkflow()
    {
        var directController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubPayoutsController.cs");
        var superAdminController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminPayoutsController.cs");
        var superAdminService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminPayoutsService.cs");

        Assert.Contains("PAYOUT_EXECUTION_WORKFLOW_REQUIRED", directController, StringComparison.Ordinal);
        Assert.Contains("Direct CommerceHub payout execution mutation is disabled", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkProcessingAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkCompletedAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_payoutService.MarkFailedAsync(", directController, StringComparison.Ordinal);

        Assert.Contains("api/super-admin/payouts", superAdminController, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync", superAdminService, StringComparison.Ordinal);
        Assert.Contains("NormalizeActorId", superAdminService, StringComparison.Ordinal);
        Assert.Contains("EnsurePayoutCompletionEvidencePresent", superAdminService, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", superAdminService, StringComparison.Ordinal);
        Assert.Contains("RecordPayoutAsync", superAdminService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
