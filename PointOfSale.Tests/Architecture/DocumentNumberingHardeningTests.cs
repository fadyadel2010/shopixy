using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class DocumentNumberingHardeningTests
{
    [Fact]
    public void CriticalDocumentNumberingPaths_MustUseSequenceBackedAllocation_AndReturnRetryHardening()
    {
        var salesReturnsPath = RepoFile("PointOfSale.API", "Controllers", "SalesReturnsController.cs");
        var purchaseReturnsPath = RepoFile("PointOfSale.API", "Controllers", "PurchaseReturnsController.cs");
        var ordersPath = RepoFile("PointOfSale.API", "Controllers", "OrdersController.cs");
        var stockTransfersPath = RepoFile("PointOfSale.API", "Controllers", "StockTransfersController.cs");
        var checkoutPath = RepoFile("PointOfSale.API", "Services", "Checkout", "CheckoutService.cs");

        // Skip test if source files are not present (e.g., in CI/published test assemblies)
        if (salesReturnsPath == null || purchaseReturnsPath == null || ordersPath == null || 
            stockTransfersPath == null || checkoutPath == null)
        {
            return;
        }

        var salesReturnsSource = File.ReadAllText(salesReturnsPath);
        var purchaseReturnsSource = File.ReadAllText(purchaseReturnsPath);
        var ordersSource = File.ReadAllText(ordersPath);
        var stockTransfersSource = File.ReadAllText(stockTransfersPath);
        var checkoutSource = File.ReadAllText(checkoutPath);

        Assert.Contains("DocumentNumberingSupport.AllocateReturnNumberAsync", salesReturnsSource, StringComparison.Ordinal);
        Assert.Contains("DocumentNumberingSupport.SaveReturnChangesWithRetryAsync", salesReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateReturnNumber()", salesReturnsSource, StringComparison.Ordinal);

        Assert.Contains("DocumentNumberingSupport.AllocateReturnNumberAsync", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.Contains("DocumentNumberingSupport.SaveReturnChangesWithRetryAsync", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateReturnNumber()", purchaseReturnsSource, StringComparison.Ordinal);

        Assert.Contains("DocumentNumberingSupport.GetNextSequenceValueAsync", ordersSource, StringComparison.Ordinal);
        Assert.Contains("DocumentNumberingSupport.GetNextSequenceValueAsync", stockTransfersSource, StringComparison.Ordinal);
        Assert.Contains("DocumentNumberingSupport.GetNextSequenceValueAsync", checkoutSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReliableDocumentNumberMigration_MustCreateSequences_AndTenantScopedUniqueIndexes()
    {
        var migrationPath = RepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260409164612_AddReliableDocumentNumberSequences.sql");

        // Skip test if source files are not present (e.g., in CI/published test assemblies)
        if (migrationPath == null)
        {
            return;
        }

        var migrationSql = File.ReadAllText(migrationPath);

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Returns_TenantId_ReturnNumber\"", migrationSql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_StockTransfers_TenantId_TransferNumber\"", migrationSql, StringComparison.Ordinal);
        Assert.Contains("CREATE SEQUENCE IF NOT EXISTS return_number_seq", migrationSql, StringComparison.Ordinal);
        Assert.Contains("CREATE SEQUENCE IF NOT EXISTS order_number_seq", migrationSql, StringComparison.Ordinal);
        Assert.Contains("CREATE SEQUENCE IF NOT EXISTS stock_transfer_number_seq", migrationSql, StringComparison.Ordinal);
        Assert.Contains("20260409164612_AddReliableDocumentNumberSequences", migrationSql, StringComparison.Ordinal);
    }

    // Returns the full path to a repository source file, or null if it doesn't exist.
    // This guards architecture tests when source files are not present (e.g., in CI/published test assemblies).
    private static string? RepoFile(params string[] relativeSegments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeSegments)));

        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }
}
