namespace PointOfSale.Tests.Architecture;

public sealed class PaymentScopeIsolationAuthorityCertificationTests
{
    [Fact]
    public void PaymentScopeIsolation_MustBindSelectedRuntimeScopeToCurrentBackendAuthority()
    {
        var refundAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts");
        var refundPage = ReadRepoFile("PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx");
        var payoutAuthority = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/payoutFinancialAuthority.ts");
        var payoutPage = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");

        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection", refundAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.order.orderId.toLowerCase() !== currentSelection.orderId.toLowerCase()", refundAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.item.vendorId.toLowerCase() !== currentSelection.vendorId.toLowerCase()", refundAuthority, StringComparison.Ordinal);
        Assert.Contains("refund.customer.customerId.toLowerCase() !== currentSelection.customerId.toLowerCase()", refundAuthority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)", refundPage, StringComparison.Ordinal);

        Assert.Contains("requireCurrentPayoutMethodSelection", payoutAuthority, StringComparison.Ordinal);
        Assert.Contains("Super Admin payout scope authority rejected stale payout-method selection", payoutAuthority, StringComparison.Ordinal);
        Assert.Contains("payoutMethodId: requireCurrentPayoutMethodSelection(executeModal.payoutMethodId, currentExecuteItem).id", payoutPage, StringComparison.Ordinal);
        Assert.Contains("!resolveSelectedMethod()", payoutPage, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
