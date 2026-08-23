using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.Bulk;

public enum BulkPreparedItemState
{
    Prepared,
    NotFound,
    ConcurrencyConflict
}

public sealed record BulkPreparedItem(
    Guid ProductId,
    uint ExpectedVersion,
    uint? CurrentRowVersion,
    bool? IsActive,
    PublishStatus? PublishStatus,
    bool? IsOnlineVisible,
    BulkPreparedItemState State)
{
    public static BulkPreparedItem Prepared(
        Guid productId,
        uint expectedVersion,
        uint currentRowVersion,
        bool isActive,
        PublishStatus publishStatus,
        bool isOnlineVisible) =>
        new(productId, expectedVersion, currentRowVersion, isActive, publishStatus, isOnlineVisible, BulkPreparedItemState.Prepared);

    public static BulkPreparedItem NotFound(Guid productId, uint expectedVersion) =>
        new(productId, expectedVersion, null, null, null, null, BulkPreparedItemState.NotFound);

    public static BulkPreparedItem ConcurrencyConflict(Guid productId, uint expectedVersion, uint currentRowVersion) =>
        new(productId, expectedVersion, currentRowVersion, null, null, null, BulkPreparedItemState.ConcurrencyConflict);
}

public sealed record BulkPreparationOutcome(
    bool IsValid,
    string CorrelationId,
    int Requested,
    int Accepted,
    IReadOnlyList<BulkPreparedItem> PreparedItems,
    IReadOnlyList<BulkRequestValidationError> Errors);
