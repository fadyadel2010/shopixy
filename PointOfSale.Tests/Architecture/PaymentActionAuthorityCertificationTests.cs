namespace PointOfSale.Tests.Architecture;

public sealed class PaymentActionAuthorityCertificationTests
{
    [Fact]
    public void CustomerPaymentActions_MustRemainMountedDuringBackgroundRefreshAndRevalidateBeforeMutation()
    {
        var orderDetails = ReadRepoFile("PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx");
        var recoveryPage = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");
        var sessionBootstrap = ReadRepoFile("PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx");

        foreach (var page in new[] { orderDetails, recoveryPage })
        {
            Assert.Contains("if (orderQuery.isLoading)", page, StringComparison.Ordinal);
            Assert.DoesNotContain("if (orderQuery.isLoading || orderQuery.isFetching)", page, StringComparison.Ordinal);

            var refetchIndex = page.IndexOf("const refreshedOrder = await orderQuery.refetch()", StringComparison.Ordinal);
            var initiationIndex = page.IndexOf("checkoutService.initiatePayment(", StringComparison.Ordinal);
            Assert.True(refetchIndex >= 0, "Payment continuation must re-enter current backend order authority.");
            Assert.True(initiationIndex > refetchIndex, "Payment initiation must remain behind the current-order refetch.");
            Assert.Contains("beginPaymentInitiationTrace", page, StringComparison.Ordinal);
            Assert.Contains("order-refetch-started", page, StringComparison.Ordinal);
            Assert.Contains("order-refetch-completed", page, StringComparison.Ordinal);
            Assert.Contains("checkout-service-entered", page, StringComparison.Ordinal);
            Assert.Contains("initiation-authority-accepted", page, StringComparison.Ordinal);
        }

        var httpClient = ReadRepoFile("PointOfSale.CommerceHub/src/api/http-client.ts");
        Assert.Contains("http-request-interceptor", httpClient, StringComparison.Ordinal);
        Assert.Contains("http-response-received", httpClient, StringComparison.Ordinal);
        Assert.Contains("http-response-failed", httpClient, StringComparison.Ordinal);

        Assert.Contains("if (document.visibilityState === \"visible\") void revalidateSession();", sessionBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"focus\", handleBackgroundRevalidation)", sessionBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"online\", handleBackgroundRevalidation)", sessionBootstrap, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"popstate\", handleHistoryNavigation)", sessionBootstrap, StringComparison.Ordinal);
        Assert.Contains("void revalidateSession(true);", sessionBootstrap, StringComparison.Ordinal);
    }

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
        Assert.Contains("!currentExecuteItem.canPreparePayout", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("!currentExecuteItem.canCompletePayout", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("Current backend payout authority is no longer available", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("prepareSuperAdminVendorPayout(currentExecuteItem.vendorId", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("completeSuperAdminVendorPayout(", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("executeModal && currentExecuteItem", payoutsPage, StringComparison.Ordinal);
        Assert.Contains("currentExecuteItem.activePayout", payoutsPage, StringComparison.Ordinal);
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
