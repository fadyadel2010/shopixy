namespace PointOfSale.Tests.Architecture;

public sealed class C2InventoryReliabilitySourceTests
{
    [Fact]
    public void ReservationMutationLocks_MustMaterializeXminAndUseWaiterSafeIsolation()
    {
        var repository = ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs");

        Assert.Contains("SELECT *, xmin FROM \"\"ProductVariants\"\"", repository, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.ReadCommitted", repository, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", repository, StringComparison.Ordinal);
        Assert.Contains("OrderBy(stock => stock.WarehouseId)", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseReturnProductLock_MustMaterializePostgresXmin()
    {
        var controller = ReadRepoFile(
            "PointOfSale.API/Controllers/PurchaseReturnsController.cs");
        var methodStart = controller.IndexOf("LockPurchaseCostProductsAsync", StringComparison.Ordinal);
        var methodEnd = controller.IndexOf("private static decimal RoundCurrency", methodStart, StringComparison.Ordinal);
        var method = controller[methodStart..methodEnd];

        Assert.Contains("SELECT *, xmin", method, StringComparison.Ordinal);
        Assert.Contains("FROM \"\"Products\"\"", method, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
