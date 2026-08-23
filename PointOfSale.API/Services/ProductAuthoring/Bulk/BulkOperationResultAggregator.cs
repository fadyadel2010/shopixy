using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces.Bulk;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkOperationResultAggregator : IBulkOperationResultAggregator
{
    public BulkOperationResult Aggregate(
        int requested,
        int accepted,
        string correlationId,
        IReadOnlyList<BulkItemResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (requested < 0 || accepted < 0 || accepted > requested)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted), "Bulk request counts are inconsistent.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        if (results.Count != accepted)
        {
            throw new ArgumentException("Every accepted bulk item must have exactly one result.", nameof(results));
        }

        var processed = 0;
        var unchanged = 0;
        var failed = 0;

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case BulkItemStatus.Processed:
                    processed++;
                    break;
                case BulkItemStatus.Unchanged:
                    unchanged++;
                    break;
                case BulkItemStatus.Failed:
                    failed++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown bulk item status '{result.Status}' during aggregation.");
            }
        }

        return new BulkOperationResult(
            Requested: requested,
            Accepted: accepted,
            Processed: processed,
            Unchanged: unchanged,
            Failed: failed,
            CorrelationId: correlationId,
            Results: results);
    }
}
