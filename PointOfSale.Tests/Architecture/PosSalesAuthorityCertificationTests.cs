namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PASS P1-ED - POS Sales Authority Certification freeze.
/// Runtime sales authority must remain bound to tenant/customer/invoice/payment allocation/current SQL state.
/// </summary>
public sealed class PosSalesAuthorityCertificationTests
{
    [Fact]
    public void SalesController_MustRejectDeletedCustomersInactiveWarehousesAndUnconfiguredUnits()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "SalesController.cs");

        Assert.Contains("LoadActiveCustomerAsync(request.CustomerId.Value)", source, StringComparison.Ordinal);
        Assert.Contains("LoadActiveWarehouseAsync(request.WarehouseId)", source, StringComparison.Ordinal);
        Assert.Contains("customer.TenantId == _tenantContext.RequiredTenantId", source, StringComparison.Ordinal);
        Assert.Contains("warehouse.IsActive", source, StringComparison.Ordinal);
        Assert.Contains("Unit is not configured for product", source, StringComparison.Ordinal);
        Assert.Contains("Default unit is not configured for product", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using conversion factor 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesController_MustNotRewriteCompletedInvoicesOrTrustDtoTotals()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "SalesController.cs");

        Assert.Contains("invoice.Status != InvoiceStatus.Draft", source, StringComparison.Ordinal);
        Assert.Contains("Only draft sales invoices can be rewritten", source, StringComparison.Ordinal);
        Assert.Contains("request.DiscountPercentage", source, StringComparison.Ordinal);
        Assert.Contains("request.DiscountAmount", source, StringComparison.Ordinal);
        Assert.Contains("invoice.TotalAmount = RoundCurrency(discountableSubtotal - invoiceDiscount + invoice.TaxAmount)", source, StringComparison.Ordinal);
        Assert.Contains("Math.Abs(totalPaid - roundedTotalAmount) > 0.01m", source, StringComparison.Ordinal);
        Assert.Contains("roundedPaidAmount != roundedTotalAmount", source, StringComparison.Ordinal);
        Assert.Contains("Math.Min(paidAmount, totalAmount)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesReturnsController_MustLockOriginalInvoiceAndBoundReturnAuthority()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "SalesReturnsController.cs");

        Assert.Contains("LoadActiveWarehouseAsync(request.WarehouseId)", source, StringComparison.Ordinal);
        Assert.Contains("originalInvoice.Status != InvoiceStatus.Completed", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("Cannot return more than sold quantity", source, StringComparison.Ordinal);
        Assert.Contains("ignoredClientOverrides", source, StringComparison.Ordinal);
        Assert.Contains("priorReturnedTotal + salesReturn.TotalAmount", source, StringComparison.Ordinal);
        Assert.Contains("approvedPayoutAmount > salesReturn.TotalAmount", source, StringComparison.Ordinal);
        Assert.Contains("originalInvoice.AccountingMutationVersion++", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimSalesReturnIdempotencyAsync", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeParts)));

        return File.ReadAllText(path);
    }

}
