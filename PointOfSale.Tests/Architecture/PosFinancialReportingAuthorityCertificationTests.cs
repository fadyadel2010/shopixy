namespace PointOfSale.Tests.Architecture;

public sealed class PosFinancialReportingAuthorityCertificationTests
{
    [Fact]
    public void FinancialStatements_MustRecomputePostedLedgerHistory()
    {
        var service = ReadRepoFile("PointOfSale.Infrastructure/Accounting/FinancialReportService.cs");

        Assert.Contains("JournalStatus.Posted", service, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.RepeatableRead", service, StringComparison.Ordinal);
        Assert.Contains("GetAccountBalancesAsOfAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("l.Account.IsActive", service, StringComparison.Ordinal);
        Assert.DoesNotContain("a.TenantId == tenantId && a.IsActive", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PartyAging_MustUseAccountLedgerForReceivablesAndPayables()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var agingStart = reports.IndexOf("#region Accounts Receivable Aging", StringComparison.Ordinal);
        var agingEnd = reports.IndexOf("#region Sales Summary", agingStart, StringComparison.Ordinal);
        var agingSource = reports[agingStart..agingEnd];

        Assert.Contains("_context.AccountLedgers", agingSource, StringComparison.Ordinal);
        Assert.Contains("PartyType.Customer", agingSource, StringComparison.Ordinal);
        Assert.Contains("PartyType.Supplier", agingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.PurchaseInvoices", agingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.SupplierTransactions", agingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance", agingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("customer.Balance", agingSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PartyAging_CurrentBucket_MustStartAtTheBeginningOfTheAsOfDate()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var agingStart = reports.IndexOf("#region Accounts Receivable Aging", StringComparison.Ordinal);
        var agingEnd = reports.IndexOf("#region Sales Summary", agingStart, StringComparison.Ordinal);
        var agingSource = reports[agingStart..agingEnd];

        Assert.Equal(2, CountOccurrences(
            agingSource,
            "var currentBoundary = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);"));
        Assert.DoesNotContain("var currentBoundary = asOfDateUtc;", agingSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardFinancialCards_MustNotUseShadowBalancesOrCachedTotals()
    {
        var dashboard = ReadRepoFile("PointOfSale.API/Controllers/DashboardController.cs");

        Assert.Contains("BuildPostedRevenueQuery", dashboard, StringComparison.Ordinal);
        Assert.Contains("PartyType.Customer", dashboard, StringComparison.Ordinal);
        Assert.Contains("PartyType.Supplier", dashboard, StringComparison.Ordinal);
        Assert.Contains("stock.Variant.CostPrice", dashboard, StringComparison.Ordinal);
        Assert.Contains("return factory(cancellationToken);", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("customer.Balance", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("stock.Variant.Product.SalePrice", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("_cache.GetOrCreateAsync", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialDashboard_MustNotCacheLedgerAggregates()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/FinancialReportsController.cs");

        Assert.Contains("return factory(cancellationToken);", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_cache.GetOrCreateAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryValuation_MustUseCurrentInventoryCostAuthority()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var valuationStart = reports.IndexOf("#region Inventory Valuation", StringComparison.Ordinal);
        var valuationEnd = reports.IndexOf("#region Daily Close", valuationStart, StringComparison.Ordinal);
        var valuationSource = reports[valuationStart..valuationEnd];

        Assert.Contains("_context.ProductVariantStocks", valuationSource, StringComparison.Ordinal);
        Assert.Contains("stock.Variant.CostPrice", valuationSource, StringComparison.Ordinal);
        Assert.Contains("stock.Warehouse.IsActive", valuationSource, StringComparison.Ordinal);
        Assert.Contains("stock.Quantity - stock.ReservedQuantity", valuationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchasePrice", valuationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SalePrice", valuationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WarehouseStocks", valuationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Cogs_MustUseImmutableSnapshotsDerivedFromVariantCostAuthority()
    {
        var sales = ReadRepoFile("PointOfSale.API/Controllers/SalesController.cs");
        var orders = ReadRepoFile("PointOfSale.API/Controllers/OrdersController.cs");
        var accounting = ReadRepoFile("PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs");

        Assert.Contains("Math.Round(variant.CostPrice, 6", sales, StringComparison.Ordinal);
        Assert.Contains("CostPriceSnapshot = costPriceSnapshot", sales, StringComparison.Ordinal);
        Assert.Contains("CostAmountSnapshot = costAmountSnapshot", sales, StringComparison.Ordinal);
        Assert.Contains("CostPrice = item.Variant?.CostPrice ?? 0m", orders, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Product.PurchasePrice", orders, StringComparison.Ordinal);
        Assert.Contains("if (item.CostAmountSnapshot.HasValue)", accounting, StringComparison.Ordinal);
        Assert.Contains("return item.CostAmountSnapshot.Value;", accounting, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Product?.PurchasePrice", accounting, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnReports_MustNotFallBackToMutableCurrentVariantCost()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var salesReturnsStart = reports.IndexOf("#region Sales Returns Report", StringComparison.Ordinal);
        var purchaseReturnsEnd = reports.IndexOf("#region Cost Analysis", salesReturnsStart, StringComparison.Ordinal);
        var returnsSource = reports[salesReturnsStart..purchaseReturnsEnd];

        Assert.Contains("item.CostAmountSnapshot ?? 0m", returnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductVariant.CostPrice", returnsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMovementOpeningBalance_MustUseMovementLedgerNotCurrentStockFallback()
    {
        var reports = ReadRepoFile("PointOfSale.API/Controllers/ReportsController.cs");
        var movementStart = reports.IndexOf("#region Product Movement", StringComparison.Ordinal);
        var movementEnd = reports.IndexOf("#region Accounts Receivable Aging", movementStart, StringComparison.Ordinal);
        var movementSource = reports[movementStart..movementEnd];

        Assert.Contains("_context.StockMovements", movementSource, StringComparison.Ordinal);
        Assert.Contains("sm.CreatedAt < startDate", movementSource, StringComparison.Ordinal);
        Assert.Contains("sm.QuantityAfter", movementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.ProductVariantStocks", movementSource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            relativePath));

        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
