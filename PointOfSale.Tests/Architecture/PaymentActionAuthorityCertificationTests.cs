namespace PointOfSale.Tests.Architecture;

public sealed class PaymentActionAuthorityCertificationTests
{
    [Fact]
    public void CustomerRefundActions_MustRebindModalCandidatesToCurrentBackendOrderAndPayoutAuthority()
    {
        var orderDetails = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var refundModal = ReadRepoFile("PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx");

        Assert.Contains("currentActiveRefundItem", orderDetails, StringComparison.Ordinal);
        Assert.Contains("currentItem?.canRefund", orderDetails, StringComparison.Ordinal);
        Assert.Contains("setActiveRefundItem(null)", orderDetails, StringComparison.Ordinal);
        Assert.Contains("item={currentActiveRefundItem}", orderDetails, StringComparison.Ordinal);
        Assert.Contains("!selectedPayoutDetailsId || !selectedSavedPayoutDetails", refundModal, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminPayoutActions_MustExecuteOnlyAgainstCurrentBackendQueueAuthority()
    {
        var payoutsPage = ReadRepoFile("PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx");

        Assert.Contains("currentExecuteItem", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("!currentExecuteItem?.canRecordCompletedPayout", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("Current backend payout authority is no longer available", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("executeSuperAdminVendorPayout(currentExecuteItem.vendorId", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("executeModal && currentExecuteItem", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("!currentExecuteItem.canRecordCompletedPayout", payoutsPage, StringComparison.Ordinal);
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
