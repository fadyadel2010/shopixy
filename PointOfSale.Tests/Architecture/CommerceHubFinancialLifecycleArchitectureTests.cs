namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubFinancialLifecycleArchitectureTests
{
    [Fact]
    public void PayoutService_MustGateCreationProcessingAndCompletionOnLedgerConfirmation()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));

        var createPayoutMethod = ExtractMethodBody(source, "CreatePayoutAsync");
        Assert.NotNull(createPayoutMethod);
        AssertContainsAndOrdered(
            createPayoutMethod,
            "EnsureEarningsHaveLedgerConfirmationAsync",
            "_dbContext.CommerceHubPayouts.Add",
            "Payout creation must require source ledger confirmation before reserving earnings into a payout.");

        var markProcessingMethod = ExtractMethodBody(source, "MarkProcessingAsync");
        Assert.NotNull(markProcessingMethod);
        AssertContainsAndOrdered(
            markProcessingMethod,
            "CaptureAndValidatePayoutFinancialReadinessAsync",
            "payout.Status = CommerceHubPayoutStatus.Processing",
            "Payout processing must validate ledger-backed cash/payable readiness before external payment can start.");

        var markCompletedMethod = ExtractMethodBody(source, "MarkCompletedAsync");
        Assert.NotNull(markCompletedMethod);
        AssertContainsAndOrdered(
            markCompletedMethod,
            "CapturePayoutFinancialStateForCompletionAsync",
            "ApplyCompletedPayoutAllocation",
            "Payout completion must capture ledger-backed state before marking earnings paid out.");
        AssertContainsAndOrdered(
            markCompletedMethod,
            "!IsExternallyLockedPayoutStatus(payout.Status)",
            "CapturePayoutFinancialStateForCompletionAsync",
            "Payout completion must require a durable processing or reconciliation-required state before external transfer evidence can be finalized.");
        AssertContainsAndOrdered(
            markCompletedMethod,
            "EnsureExternalPayoutEvidenceIsUniqueAsync",
            "ApplyCompletedPayoutAllocation",
            "Payout completion must reject reused external transfer evidence before any local earning mutation.");
        AssertContainsAndOrdered(
            markCompletedMethod,
            "EnsureExternalPayoutEvidenceIsUniqueAsync",
            "payout.Status = CommerceHubPayoutStatus.Completed",
            "Payout completion must reject reused external transfer evidence before local completion.");

        Assert.Contains("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", source, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_MUST_BE_PROCESSING_BEFORE_COMPLETION", source, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundService_MustValidateProviderConfirmationBeforeFinancialReversal()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubRefundService.cs"));

        var confirmRefundMethod = ExtractMethodBody(source, "ConfirmRefundAsync");
        Assert.NotNull(confirmRefundMethod);
        AssertContainsAndOrdered(
            confirmRefundMethod,
            "ValidateGatewayRefundConfirmation",
            "CompleteRefundAsync",
            "External refund confirmation must validate provider status/reference before writing refund ledger entries or reversing vendor earnings.");

        var completionMethod = ExtractMethodBody(source, "CompleteRefundAsync");
        Assert.NotNull(completionMethod);
        Assert.Contains("RecordRefundAsync", completionMethod, StringComparison.Ordinal);

        Assert.Contains("REFUND_GATEWAY_CONFIRMATION_REQUIRED", source, StringComparison.Ordinal);
        Assert.Contains("REFUND_PROVIDER_REFERENCE_MISMATCH", source, StringComparison.Ordinal);
        Assert.Contains("IsConfirmedGatewayRefundStatus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFinancialLedger_MustRejectSilentCurrencyFallbacksAndNonTerminalMoneyFacts()
    {
        var ledgerService = File.ReadAllText(ApiPath("Services", "CommerceHub", "FinancialLedger", "CommerceHubFinancialLedgerService.cs"));
        var safetyGuard = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubFinancialSafetyGuardService.cs"));
        var decisionEngine = File.ReadAllText(ApiPath("Services", "CommerceHub", "RefundFinancialDecisionEngine.cs"));
        var financeRead = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinanceReadService.cs"));
        var payoutRead = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPayoutReadService.cs"));
        var vendorPayments = File.ReadAllText(ApiPath("Services", "CommerceHub", "Finance", "CommerceHubVendorPaymentsService.cs"));
        var superAdminPayoutOrchestration = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutOrchestrationService.cs"));
        var transactionEntity = File.ReadAllText(CorePath("Entities", "CommerceHubFinance", "CommerceHubFinancialTransaction.cs"));

        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", transactionEntity, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("INSERT OR IGNORE INTO \"\"CommerceHubFinancialGuardLocks\"\"", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"\"Scope\"\") DO NOTHING", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", safetyGuard, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", decisionEngine, StringComparison.Ordinal);
        Assert.Contains("ResolveLedgerCurrency", financeRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.NormalizeIfPresent", financeRead, StringComparison.Ordinal);
        Assert.Contains("PendingPayouts: 0m", financeRead, StringComparison.Ordinal);
        Assert.Contains("PendingPayoutsTotal", payoutRead, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireEgpCurrency", vendorPayments, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_CURRENCY_REQUIRED", vendorPayments, StringComparison.Ordinal);
        Assert.Contains("ICommerceHubPayoutService", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("NormalizeAmount", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCurrency", transactionEntity, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCurrency", payoutRead, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCurrency", vendorPayments, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"EGP\"", safetyGuard, StringComparison.Ordinal);
        Assert.DoesNotContain("? \"EGP\"", decisionEngine, StringComparison.Ordinal);
        Assert.DoesNotContain("?? \"EGP\"", financeRead, StringComparison.Ordinal);

        Assert.Contains("ValidatePayoutFinancialIntegrity", ledgerService, StringComparison.Ordinal);
        Assert.Contains("payout.Status != CommerceHubPayoutStatus.Completed", ledgerService, StringComparison.Ordinal);
        Assert.Contains("ValidateRefundFinancialIntegrity", ledgerService, StringComparison.Ordinal);
        Assert.Contains("refund.Status != CommerceHubRefundStatus.Completed", ledgerService, StringComparison.Ordinal);
        Assert.Contains("refund.TenantId != subOrder.StoreId", ledgerService, StringComparison.Ordinal);
        Assert.Contains("refund.SubOrderId != subOrder.Id", ledgerService, StringComparison.Ordinal);
        Assert.Contains("subOrder.MarketplaceOrderId != marketplaceOrder.Id", ledgerService, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutExecution_MustNotBypassPerEarningLedgerConfirmation()
    {
        var superAdminPayoutOrchestration = File.ReadAllText(ApiPath("SuperAdmin", "Services", "SuperAdminPayoutOrchestrationService.cs"));
        var payoutService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubPayoutService.cs"));
        var tests = File.ReadAllText(TestsPath("CommerceHub", "SuperAdminPayoutOrchestrationServiceTests.cs"));

        Assert.Contains("ICommerceHubPayoutService", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("CreatePayoutAsync", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("MarkProcessingAsync", superAdminPayoutOrchestration, StringComparison.Ordinal);
        Assert.Contains("MarkCompletedAsync", superAdminPayoutOrchestration, StringComparison.Ordinal);

        Assert.Contains("EnsureEarningsHaveLedgerConfirmationAsync", payoutService, StringComparison.Ordinal);
        Assert.Contains("PAYOUT_LEDGER_CONFIRMATION_REQUIRED", payoutService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialTransactionType.OrderPayment", payoutService, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialReferenceType.Order", payoutService, StringComparison.Ordinal);
        Assert.Contains("positiveOrderSubOrderIds.Contains(transaction.ReferenceId)", payoutService, StringComparison.Ordinal);

        Assert.Contains("ExecuteAsync_DoesNotTranslateCanonicalBusinessExceptions", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubFinancialWorkflow_MustFailClosedOnSnapshotArithmeticDrift()
    {
        var workflowService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));

        Assert.Contains("ValidateCommissionSnapshotJsonMatchesFields", workflowService, StringComparison.Ordinal);
        Assert.Contains("EnsureFinancialSnapshotIntegrity", workflowService, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_SNAPSHOT_MISMATCH", workflowService, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_FINANCIAL_ARITHMETIC_MISMATCH", workflowService, StringComparison.Ordinal);
        Assert.Contains("grossCommissionAmount", workflowService, StringComparison.Ordinal);
        Assert.Contains("finalCommissionAmount", workflowService, StringComparison.Ordinal);
        Assert.Contains("promoDiscountAmount", workflowService, StringComparison.Ordinal);
        Assert.Contains("vendor earning must equal sub-order total minus gross platform commission", workflowService, StringComparison.Ordinal);

        var applyFinancialSnapshotMethod = ExtractMethodBody(workflowService, "ApplyFinancialSnapshot");
        Assert.NotNull(applyFinancialSnapshotMethod);
        AssertContainsAndOrdered(
            applyFinancialSnapshotMethod,
            "ValidateCommissionSnapshotJsonMatchesFields",
            "EnsureFinancialSnapshotIntegrity",
            "Complete commission snapshots must be validated before financial workflow trusts them.");
        AssertContainsAndOrdered(
            applyFinancialSnapshotMethod,
            "EnsureFinancialSnapshotIntegrity",
            "return;",
            "Financial snapshot validation must short-circuit the method on success.");
        AssertContainsAndOrdered(
            applyFinancialSnapshotMethod,
            "EnsureFinancialSnapshotIntegrity",
            "BuildCommissionSnapshotJson",
            "Backfilled financial snapshots must pass arithmetic invariants before becoming persisted truth.");
    }

    [Fact]
    public void CommerceHubVendorEarnings_MustNotRepriceNonPendingEarnings()
    {
        var workflowService = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubFinancialWorkflowService.cs"));

        Assert.Contains("EnsureExistingEarningMatchesFinancialSnapshot", workflowService, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_EARNING_AMOUNT_LOCKED", workflowService, StringComparison.Ordinal);
        Assert.Contains("COMMERCEHUB_EARNING_PAYMENT_MISMATCH", workflowService, StringComparison.Ordinal);
        Assert.Contains("earning.Status == CommerceHubVendorEarningStatus.Pending", workflowService, StringComparison.Ordinal);

        var ensureEarningMethod = ExtractMethodBody(workflowService, "EnsureEarningRecord");
        Assert.NotNull(ensureEarningMethod);
        AssertContainsAndOrdered(
            ensureEarningMethod,
            "EnsureExistingEarningMatchesFinancialSnapshot",
            "earning.Status == CommerceHubVendorEarningStatus.Pending",
            "Existing earnings must be checked before any mutable pending-only synchronization.");
        AssertContainsAndOrdered(
            ensureEarningMethod,
            "earning.Status == CommerceHubVendorEarningStatus.Pending",
            "earning.Amount = subOrder.VendorEarningAmount",
            "Only pending earnings may be repriced from the sub-order financial snapshot.");
    }

    /// <summary>
    /// Asserts that both markers exist in the method body and that the first marker appears before the second.
    /// </summary>
    private static void AssertContainsAndOrdered(string methodBody, string firstMarker, string secondMarker, string failureMessage)
    {
        var firstIndex = methodBody.IndexOf(firstMarker, StringComparison.Ordinal);
        var secondIndex = methodBody.IndexOf(secondMarker, StringComparison.Ordinal);

        Assert.True(
            firstIndex != -1,
            $"First marker '{firstMarker}' not found in method body. {failureMessage}");

        Assert.True(
            secondIndex != -1,
            $"Second marker '{secondMarker}' not found in method body. {failureMessage}");

        Assert.True(
            firstIndex < secondIndex,
            $"Ordering violation: '{firstMarker}' (at {firstIndex}) must appear before '{secondMarker}' (at {secondIndex}). {failureMessage}");
    }

    private static string? ExtractMethodBody(string source, string methodName)
    {
        var searchIndex = 0;
        while (searchIndex < source.Length)
        {
            var methodIndex = source.IndexOf(methodName, searchIndex, StringComparison.Ordinal);
            if (methodIndex < 0)
            {
                return null;
            }

            var openBraceIndex = source.IndexOf('{', methodIndex);
            var semicolonIndex = source.IndexOf(';', methodIndex);
            if (openBraceIndex < 0)
            {
                return null;
            }

            if (semicolonIndex >= 0 && semicolonIndex < openBraceIndex)
            {
                searchIndex = methodIndex + methodName.Length;
                continue;
            }

            var depth = 1;
            for (var i = openBraceIndex + 1; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(methodIndex, i - methodIndex + 1);
                    }
                }
            }

            return null;
        }

        return null;
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));
    }

    private static string CorePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Core" }
                .Concat(parts)
                .ToArray()));
    }

    private static string TestsPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Tests" }
                .Concat(parts)
                .ToArray()));
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
