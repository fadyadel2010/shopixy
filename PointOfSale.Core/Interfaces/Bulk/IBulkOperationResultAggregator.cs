using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Core.Interfaces.Bulk;

public interface IBulkOperationResultAggregator
{
    BulkOperationResult Aggregate(
        int requested,
        int accepted,
        string correlationId,
        IReadOnlyList<BulkItemResult> results);
}
