namespace PointOfSale.Tests.Architecture;

public sealed class PaymentOwnershipAuthorityCertificationTests
{
    [Fact]
    public void CustomerPaymentRecovery_MustReenterCurrentBackendOrderAuthority()
    {
        var recovery = ReadRepoFile("PointOfSale.CommerceHub/src/pages/OrderPaymentRecoveryPage.tsx");
        var context = ReadRepoFile("PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts");

        Assert.Contains("refreshedOrder = await orderQuery.refetch()", recovery, StringComparison.Ordinal);
        Assert.Contains("!currentOrder?.canRetryPayment", recovery, StringComparison.Ordinal);
        Assert.Contains("return null;", context, StringComparison.Ordinal);
        Assert.Contains("Mutation/list data does not create a durable recovery identity.", context, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdminMonitoring_MustRemainValidatedVisibilityNotSelectedOwnership()
    {
        var authority = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/paymentOwnershipAuthority.ts");
        var service = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/paymentsService.ts");
        var page = ReadRepoFile("PointOfSale.Web/src/super-admin/payments/PaymentsPage.tsx");

        Assert.Contains("requireSuperAdminPaymentsPresentation", authority, StringComparison.Ordinal);
        Assert.Contains("rejected duplicate payment monitoring evidence", authority, StringComparison.Ordinal);
        Assert.Contains("requireCurrentSuperAdminPaymentSelection", authority, StringComparison.Ordinal);
        Assert.Contains("requireSuperAdminPaymentsPresentation", service, StringComparison.Ordinal);
        Assert.Contains("paymentRequestId: request.paymentRequestId", page, StringComparison.Ordinal);
        Assert.Contains("currentActionRequest", page, StringComparison.Ordinal);
        Assert.Contains("!paymentsQuery.isFetching && !currentActionRequest", page, StringComparison.Ordinal);
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
