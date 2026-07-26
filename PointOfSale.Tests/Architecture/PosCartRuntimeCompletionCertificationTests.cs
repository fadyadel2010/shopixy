namespace PointOfSale.Tests.Architecture;

public sealed class PosCartRuntimeCompletionCertificationTests
{
    [Fact]
    public void PosCartRuntime_MustBeFirstClassSqlAuthorityWithLifecycleAndOutbox()
    {
        var context = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var service = ReadRepoFile("PointOfSale.API/Services/Pos/PosCartRuntimeService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PosCartsController.cs");
        var migration = Directory
            .GetFiles(Path.Combine(RepoRoot(), "PointOfSale.Infrastructure", "Migrations"), "*_PosCartRuntime.cs")
            .Select(File.ReadAllText)
            .Single();

        Assert.Contains("DbSet<PosCart> PosCarts", context, StringComparison.Ordinal);
        Assert.Contains("DbSet<PosCartItem> PosCartItems", context, StringComparison.Ordinal);
        Assert.Contains("DbSet<PosCartRequestIdempotency> PosCartRequestIdempotencyRecords", context, StringComparison.Ordinal);
        Assert.Contains("DbSet<PosCartOutboxMessage> PosCartOutboxMessages", context, StringComparison.Ordinal);
        Assert.Contains("PosCartStatus.Active or PosCartStatus.Recovered", service, StringComparison.Ordinal);
        Assert.Contains("PrepareCheckoutAsync", service, StringComparison.Ordinal);
        Assert.Contains("CompleteFromSaleAsync", service, StringComparison.Ordinal);
        Assert.Contains("StageOutbox", service, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", controller, StringComparison.Ordinal);
        Assert.Contains("CreateTable(", migration, StringComparison.Ordinal);
        Assert.Contains("PosCarts", migration, StringComparison.Ordinal);
        Assert.Contains("PosCartItems", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PosSaleCheckout_MustCompleteServerCartInsideSaleTransaction()
    {
        var sales = ReadRepoFile("PointOfSale.API/Controllers/SalesController.cs");

        Assert.Contains("PrepareCheckoutAsync(", sales, StringComparison.Ordinal);
        Assert.Contains("CompleteFromSaleAsync(", sales, StringComparison.Ordinal);
        Assert.Contains("X-Pos-Cart-Id", sales, StringComparison.Ordinal);
        Assert.Contains("X-Pos-Cart-Id header is required", sales, StringComparison.Ordinal);
        Assert.Contains("X-Pos-Cart-Id must be a valid POS cart id", sales, StringComparison.Ordinal);
        Assert.Contains("var createIdempotencyKey = normalizedPosCartId;", sales, StringComparison.Ordinal);
        Assert.Contains("request = cartCheckout.Request;", sales, StringComparison.Ordinal);
        Assert.DoesNotContain("normalizedPosCartId ?? normalizedPaymentAttemptId", sales, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.TryParse(normalizedPosCartId, out var parsedCartId) && _posCartRuntimeService != null", sales, StringComparison.Ordinal);
    }

    [Fact]
    public void PosCartCheckoutReplay_MustAllowCompletedCartToReachSaleIdempotencyReplay()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Pos/PosCartRuntimeService.cs");

        Assert.Contains("PosCartStatus.Active or PosCartStatus.Recovered or PosCartStatus.Completed", service, StringComparison.Ordinal);
        Assert.Contains("cart.Status == PosCartStatus.Completed && !cart.CompletedSaleId.HasValue", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PosCartRuntime_MustNotDependOnStorefrontCommerceHubOrInventoryReservations()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Pos/PosCartRuntimeService.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/PosCartsController.cs");

        Assert.DoesNotContain("CheckoutService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHub", service, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReservation", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Storefront", controller, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (current.GetFiles("PointOfSale.sln").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved.");
    }
}
