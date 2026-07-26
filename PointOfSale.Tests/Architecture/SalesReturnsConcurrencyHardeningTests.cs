using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class SalesReturnsConcurrencyHardeningTests
{
    [Fact]
    public void SalesReturns_CreateFlow_MustUseSharedStockLocking_AndAvoidDirectStockQueries()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "SalesReturnsController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);

        Assert.Contains("new StockLockTarget(", source, StringComparison.Ordinal);
        Assert.Contains("CreateIfMissing: true", source, StringComparison.Ordinal);
        Assert.Contains("DistinctBy(target => (target.WarehouseId, target.VariantId))", source, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.LockMultipleStocksAsync(", source, StringComparison.Ordinal);
        Assert.Contains(
            "stockMap.TryGetValue((request.WarehouseId, line.Snapshot.EffectiveVariantId), out var variantStock)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(", source, StringComparison.Ordinal);

        Assert.DoesNotContain("_context.ProductVariantStocks", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".FirstOrDefaultAsync(vs =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".SingleOrDefaultAsync(vs =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesReturns_CreateFlow_MustCommitRefundOutboxBeforeSynchronousAccountingPost()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "SalesReturnsController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);
        var enqueueIndex = source.IndexOf("await _accountingOutbox.EnqueuePosRefundAsync(", StringComparison.Ordinal);
        var commitIndex = source.IndexOf("await transaction.CommitAsync();", StringComparison.Ordinal);
        var postIndex = source.IndexOf("await _commerceAccounting.PostPosRefundAsync(", StringComparison.Ordinal);
        var markIndex = source.IndexOf("await MarkSynchronousRefundAccountingOutboxProcessedAsync(", StringComparison.Ordinal);
        var helperIndex = source.IndexOf("await TryCompleteSynchronousRefundAccountingAsync(", StringComparison.Ordinal);

        Assert.True(enqueueIndex >= 0, "Expected refund outbox enqueue in SalesReturnsController.");
        Assert.True(commitIndex > enqueueIndex, "Refund outbox enqueue must happen before the transaction commit.");
        Assert.True(helperIndex > commitIndex, "The synchronous refund accounting helper call must happen after the transaction commit.");
        Assert.True(postIndex > commitIndex, "Synchronous refund accounting post must happen after the transaction commit.");
        Assert.True(markIndex > postIndex, "Outbox processed marker must be written after the external refund post succeeds.");
    }

    [Fact]
    public void SalesReturns_CreateFlow_MustUseSqlFirstIdempotencyForReplaySafety()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "SalesReturnsController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);

        Assert.Contains("SalesReturnCreateOperation = \"SALES_RETURN_CREATE\"", source, StringComparison.Ordinal);
        Assert.Contains("_context.SalesRequestIdempotencyRecords", source, StringComparison.Ordinal);
        Assert.Contains("SalesRequestIdempotency", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimSalesReturnIdempotencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildStoredSalesReturnResponse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_idempotencyService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckRequestAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryAcquireLockAsync(", source, StringComparison.Ordinal);
    }

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
