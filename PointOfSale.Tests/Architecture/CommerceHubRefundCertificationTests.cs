namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRefundCertificationTests
{
    [Fact]
    public void RefundLifecycle_MustStaySqlAuthoritativeAuditedAndTransitionBounded()
    {
        var customerRefund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CustomerRefundService.cs");
        var superAdminRefunds = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminRefundsService.cs");
        var workflow = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundRequestWorkflowPolicy.cs");
        var refundStatus = ReadRepoFile("PointOfSale.Core/Enums/CommerceHubRefundStatus.cs");
        var refundRequestStatus = ReadRepoFile("PointOfSale.Core/Enums/CommerceHubRefundRequestStatus.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", customerRefund, StringComparison.Ordinal);
        Assert.Contains("EnsureCustomerRefundRequestAllowedAsync", customerRefund, StringComparison.Ordinal);
        Assert.Contains("order.CustomerId != customerId", customerRefund, StringComparison.Ordinal);
        Assert.Contains("FindRefundByIdempotencyAsync", customerRefund, StringComparison.Ordinal);
        Assert.Contains("EnsureReplayMatches", customerRefund, StringComparison.Ordinal);
        Assert.Contains("CustomerRefundRequestPolicy.Evaluate", customerRefund, StringComparison.Ordinal);
        Assert.Contains("ValidateOwnedMediaAsync", customerRefund, StringComparison.Ordinal);
        Assert.Contains("REFUND_REQUEST_CREATED", customerRefund, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_DETAILS_USED_FOR_REFUND", customerRefund, StringComparison.Ordinal);

        Assert.Contains("ApproveAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("RejectAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("RetryAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("LoadRefundForUpdateAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("EnsureReturnRequestForApprovedRefundAsync", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("REFUND_APPROVED", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("REFUND_REJECTED", superAdminRefunds, StringComparison.Ordinal);
        Assert.Contains("REFUND_RETRY_PREPARED", superAdminRefunds, StringComparison.Ordinal);

        Assert.Contains("CanMaintainReturnWorkflow", workflow, StringComparison.Ordinal);
        Assert.Contains("CanTransitionToAwaitingPayout", workflow, StringComparison.Ordinal);
        Assert.Contains("ResolveUnlockedState", workflow, StringComparison.Ordinal);
        Assert.Contains("CanExecute", workflow, StringComparison.Ordinal);
        Assert.Contains("Requested", refundRequestStatus, StringComparison.Ordinal);
        Assert.Contains("WaitingForReturn", refundRequestStatus, StringComparison.Ordinal);
        Assert.Contains("AwaitingPayout", refundRequestStatus, StringComparison.Ordinal);
        Assert.Contains("Paid", refundRequestStatus, StringComparison.Ordinal);
        Assert.Contains("ReconciliationRequired", refundStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundExecution_MustStayIdempotentReadinessGatedAndReplaySafe()
    {
        var refund = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundService.cs");
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");
        var recovery = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubRefundRecoveryHostedService.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var refundRequestConfig = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/CommerceHubRefundRequestConfiguration.cs");

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginPaymentLifecycle()", refund, StringComparison.Ordinal);
        Assert.Contains("CommerceHubBoundaryMutationScope.BeginFinancialWorkflow()", refund, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", refund, StringComparison.Ordinal);
        Assert.Contains("FindRefundByIdempotencyAsync", refund, StringComparison.Ordinal);
        Assert.Contains("ClaimPendingRefundForGatewayExecutionAsync", refund, StringComparison.Ordinal);
        Assert.Contains("RecoverProcessingRefundWithLockAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", refund, StringComparison.Ordinal);
        Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", refund, StringComparison.Ordinal);
        Assert.Contains("RecordRefundAsync", refund, StringComparison.Ordinal);

        Assert.Contains("CommerceHubBoundaryMutationScope.BeginRefund()", execution, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", execution, StringComparison.Ordinal);
        Assert.Contains("LoadRefundRequestForUpdateAsync", execution, StringComparison.Ordinal);
        Assert.Contains("LoadTrackedRefundForUpdateAsync", execution, StringComparison.Ordinal);
        Assert.Contains("LoadExecutionForUpdateAsync", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorRefundExecutionAllowedAsync", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureNoProcessingPayoutForRefundEarningsAsync", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureRefundCompletionDoesNotOverrunAsync", execution, StringComparison.Ordinal);
        Assert.Contains("RecoverReconciliationRequiredRefundsAsync", execution, StringComparison.Ordinal);

        Assert.Contains("ReconcileProcessingRefundsAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("RecoverReconciliationRequiredRefundsAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("entity.HasIndex(refund => new { refund.TenantId, refund.IdempotencyKey }).IsUnique()", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_CommerceHubRefundRequests_IdempotencyKey", refundRequestConfig, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(refundRequest => refundRequest.IdempotencyKey)", refundRequestConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundHttpMutation_MustRequireApprovedRequestWorkflow()
    {
        var directController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubRefundsController.cs");
        var superAdminController = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminRefundsController.cs");
        var superAdminService = ReadRepoFile("PointOfSale.API/SuperAdmin/Services/SuperAdminRefundsService.cs");
        var execution = ReadRepoFile("PointOfSale.API/Services/CommerceHub/RefundExecutionService.cs");

        Assert.Contains("REFUND_WORKFLOW_REQUIRED", directController, StringComparison.Ordinal);
        Assert.Contains("Direct CommerceHub refund mutation is disabled", directController, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Roles = \"Owner,Admin\")]", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_refundService.CreateRefundAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_refundService.ConfirmRefundAsync(", directController, StringComparison.Ordinal);
        Assert.DoesNotContain("_refundService.FailRefundAsync(", directController, StringComparison.Ordinal);

        Assert.Contains("api/super-admin/refunds", superAdminController, StringComparison.Ordinal);
        Assert.Contains("ApproveAsync", superAdminService, StringComparison.Ordinal);
        Assert.Contains("RejectAsync", superAdminService, StringComparison.Ordinal);
        Assert.Contains("_refundExecutionService.ExecuteAsync", superAdminService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubRefundRequestWorkflowPolicy.CanExecute", execution, StringComparison.Ordinal);
        Assert.Contains("EnsureOutwardMoneyMovementAllowedAsync", execution, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerRefundEndpoints_MustRegisterTheirRateLimitPolicies()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/CustomerRefundsController.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("[EnableRateLimiting(\"CustomerRefundRateLimit\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(\"CustomerRefundUploadRateLimit\")]", controller, StringComparison.Ordinal);
        Assert.Contains("options.AddPolicy(\"CustomerRefundRateLimit\"", program, StringComparison.Ordinal);
        Assert.Contains("options.AddPolicy(\"CustomerRefundUploadRateLimit\"", program, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
