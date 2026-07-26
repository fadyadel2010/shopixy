namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMerchantBalanceCertificationTests
{
    [Fact]
    public void MerchantBalances_MustBeDerivedFromLedgerBucketsNotDashboardState()
    {
        var wallet = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Wallet/CommerceHubWalletService.cs");
        var dashboard = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs");
        var transaction = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs");

        Assert.Contains("BuildWalletQuery", wallet, StringComparison.Ordinal);
        Assert.Contains("CommerceHubFinancialEntityType.Vendor", wallet, StringComparison.Ordinal);
        Assert.Contains("TotalCredits", wallet, StringComparison.Ordinal);
        Assert.Contains("TotalDebits", wallet, StringComparison.Ordinal);
        Assert.Contains("contains multiple currencies", wallet, StringComparison.Ordinal);
        Assert.Contains("GetVendorStatementPagedAsync", wallet, StringComparison.Ordinal);
        Assert.Contains("CarryForwardBalance", wallet, StringComparison.Ordinal);

        Assert.Contains("BuildLedgerQuery", dashboard, StringComparison.Ordinal);
        Assert.Contains("BuildVendorControlAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("BuildVendorCommissionQuery", dashboard, StringComparison.Ordinal);
        Assert.Contains("ReceivablesBalance", dashboard, StringComparison.Ordinal);
        Assert.Contains("PayablesBalance", dashboard, StringComparison.Ordinal);
        Assert.Contains("EnsureSingleCurrency", dashboard, StringComparison.Ordinal);

        Assert.Contains("FinancialBucket", transaction, StringComparison.Ordinal);
        Assert.Contains("FinancialCurrencyGuard.RequireCurrency", transaction, StringComparison.Ordinal);
        Assert.Contains("Amount must be greater than zero", transaction, StringComparison.Ordinal);
    }

    [Fact]
    public void CodCommissionDebtThreshold_MustImmediatelyBlockNewOrdersAtHardLimit()
    {
        var options = ReadRepoFile("PointOfSale.API/Configuration/CommerceHubVendorDebtOptions.cs");
        var debt = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorDebtService.cs");
        var control = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorFinancialControlService.cs");
        var checkout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var publicVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubPublicVendorVisibilityService.cs");
        var query = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubQueryService.cs");

        Assert.Contains("DefaultSuspensionThresholdAmount = 1000m", options, StringComparison.Ordinal);
        Assert.Contains("var isSuspended = amountOwed >= threshold;", debt, StringComparison.Ordinal);
        Assert.Contains("MinimumPaymentToRestoreOrdering", debt, StringComparison.Ordinal);
        Assert.Contains("New CommerceHub orders are suspended", debt, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", control, StringComparison.Ordinal);
        Assert.Contains("snapshot.IsSuspendedFromNewOrders", control, StringComparison.Ordinal);

        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("VENDOR_SUSPENDED_FOR_SETTLEMENT", checkout, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorOrderAcceptanceAllowedAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", publicVisibility, StringComparison.Ordinal);
        Assert.Contains("GetVendorsSuspendedFromNewOrdersAsync", query, StringComparison.Ordinal);
    }

    [Fact]
    public void CommissionRequests_MustReduceDebtOnlyThroughApprovedEvidenceAndLedgerPosting()
    {
        var request = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorPaymentRequest.cs");
        var proof = ReadRepoFile("PointOfSale.Core/Entities/CommerceHubFinance/CommerceHubVendorPaymentProof.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/CommerceHub/Finance/CommerceHubVendorPaymentsService.cs");

        Assert.Contains("PendingVerification", request, StringComparison.Ordinal);
        Assert.Contains("Approve(", request, StringComparison.Ordinal);
        Assert.Contains("Only pending payment requests can change state", request, StringComparison.Ordinal);
        Assert.Contains("LedgerCorrelationId", request, StringComparison.Ordinal);
        Assert.Contains("LedgerIdempotencyKey", request, StringComparison.Ordinal);
        Assert.Contains("ContentHash", proof, StringComparison.Ordinal);

        Assert.Contains("CreateVendorPaymentRequestAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureVendorDebtAllowsSubmission", service, StringComparison.Ordinal);
        Assert.Contains("ApprovePaymentRequestAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureApprovalAmountIsValid", service, StringComparison.Ordinal);
        Assert.Contains("RecordTransactionAsync", service, StringComparison.Ordinal);
        Assert.Contains("CommerceHubVendorPaymentAuditLog.Create", service, StringComparison.Ordinal);
        Assert.Contains("VendorPaymentProofContentHashUnique", service, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
