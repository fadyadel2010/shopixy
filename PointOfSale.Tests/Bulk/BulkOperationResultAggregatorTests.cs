using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.API.Services.ProductAuthoring.Bulk;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkOperationResultAggregatorTests
{
    private readonly BulkOperationResultAggregator _aggregator = new();

    [Fact]
    public void AggregatesProcessedUnchangedFailedCorrectly()
    {
        var id = Guid.NewGuid();
        var results = new[]
        {
            new BulkItemResult(id, BulkItemStatus.Processed, null, null, "1", "2"),
            new BulkItemResult(id, BulkItemStatus.Unchanged, null, null, "1", "1"),
            new BulkItemResult(id, BulkItemStatus.Failed, "NOT_FOUND", "Not found.", "2", null)
        };

        var result = _aggregator.Aggregate(3, 3, "corr-1", results);

        Assert.Equal(3, result.Requested);
        Assert.Equal(3, result.Accepted);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(1, result.Failed);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal(3, result.Results.Count);
    }

    [Fact]
    public void AggregatesAllProcessed()
    {
        var id = Guid.NewGuid();
        var results = new[]
        {
            new BulkItemResult(id, BulkItemStatus.Processed, null, null, "1", "2"),
            new BulkItemResult(id, BulkItemStatus.Processed, null, null, "3", "4")
        };

        var result = _aggregator.Aggregate(2, 2, "corr-2", results);

        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, result.Processed);
    }

    [Fact]
    public void AggregatesAllFailed()
    {
        var id = Guid.NewGuid();
        var results = new[]
        {
            new BulkItemResult(id, BulkItemStatus.Failed, "NOT_FOUND", "Not found.", "1", null),
            new BulkItemResult(id, BulkItemStatus.Failed, "CONCURRENCY_CONFLICT", "Conflict.", "2", null)
        };

        var result = _aggregator.Aggregate(2, 2, "corr-3", results);

        Assert.Equal(0, result.Processed);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(2, result.Failed);
    }

    [Fact]
    public void AggregatesEmptyResults()
    {
        var result = _aggregator.Aggregate(0, 0, "corr-4", Array.Empty<BulkItemResult>());

        Assert.Equal(0, result.Processed);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void AcceptsRequestedGreaterThanAcceptedWhenEveryAcceptedItemHasAResult()
    {
        var results = new[]
        {
            BulkItemResult.Processed(Guid.NewGuid(), 1, 2),
            BulkItemResult.Unchanged(Guid.NewGuid(), 3, 3),
            BulkItemResult.NotFound(Guid.NewGuid(), 4)
        };

        var result = _aggregator.Aggregate(5, 3, "corr-5", results);

        Assert.Equal(5, result.Requested);
        Assert.Equal(3, result.Accepted);
        Assert.Equal("corr-5", result.CorrelationId);
    }

    [Fact]
    public void RejectsMissingPerItemResults()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _aggregator.Aggregate(5, 3, "corr-5", Array.Empty<BulkItemResult>()));

        Assert.Equal("results", error.ParamName);
    }

    [Fact]
    public void PreservesSuppliedOrder()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var results = new[]
        {
            new BulkItemResult(id1, BulkItemStatus.Failed, "NOT_FOUND", "Not found.", "1", null),
            new BulkItemResult(id2, BulkItemStatus.Processed, null, null, "2", "3"),
            new BulkItemResult(id3, BulkItemStatus.Unchanged, null, null, "4", "4")
        };

        var result = _aggregator.Aggregate(3, 3, "corr-6", results);

        Assert.Equal(id1, result.Results[0].ProductId);
        Assert.Equal(id2, result.Results[1].ProductId);
        Assert.Equal(id3, result.Results[2].ProductId);
    }
}
