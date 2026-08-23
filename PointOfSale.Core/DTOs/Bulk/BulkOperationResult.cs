using System.Globalization;
using System.Text.Json.Serialization;

namespace PointOfSale.Core.DTOs.Bulk;

public sealed record BulkItemResult(
    Guid ProductId,
    BulkItemStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PreviousVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NewVersion)
{
    public static BulkItemResult Processed(Guid productId, uint expectedVersion, uint? newVersion = null) =>
        new(
            productId,
            BulkItemStatus.Processed,
            null,
            null,
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            newVersion?.ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult Unchanged(Guid productId, uint expectedVersion, uint currentRowVersion) =>
        new(
            productId,
            BulkItemStatus.Unchanged,
            null,
            null,
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            currentRowVersion.ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult AlreadyActive(
        Guid productId,
        uint expectedVersion,
        uint? currentRowVersion = null) =>
        new(
            productId,
            BulkItemStatus.Unchanged,
            BulkErrorCodes.AlreadyActive,
            "Product is already active.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            (currentRowVersion ?? expectedVersion).ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult AlreadyInactive(
        Guid productId,
        uint expectedVersion,
        uint? currentRowVersion = null) =>
        new(
            productId,
            BulkItemStatus.Unchanged,
            BulkErrorCodes.AlreadyInactive,
            "Product is already inactive.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            (currentRowVersion ?? expectedVersion).ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult AlreadyPublished(
        Guid productId,
        uint expectedVersion,
        uint? currentRowVersion = null) =>
        new(
            productId,
            BulkItemStatus.Unchanged,
            BulkErrorCodes.AlreadyPublished,
            "Product is already published.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            (currentRowVersion ?? expectedVersion).ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult AlreadyUnpublished(
        Guid productId,
        uint expectedVersion,
        uint? currentRowVersion = null) =>
        new(
            productId,
            BulkItemStatus.Unchanged,
            BulkErrorCodes.AlreadyUnpublished,
            "Product is already unpublished.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            (currentRowVersion ?? expectedVersion).ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult ProductInactive(
        Guid productId,
        uint expectedVersion) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.ProductInactive,
            "Product is inactive and cannot be published.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult PublicationNotReady(
        Guid productId,
        uint expectedVersion,
        string message) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.PublicationNotReady,
            message,
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult NotFound(Guid productId, uint expectedVersion) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.NotFound,
            "Product was not found.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult ConcurrencyConflict(Guid productId, uint expectedVersion, uint currentRowVersion) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.ConcurrencyConflict,
            "Product has been modified by another operation.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            currentRowVersion.ToString(CultureInfo.InvariantCulture));

    public static BulkItemResult Forbidden(Guid productId, uint expectedVersion) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.Forbidden,
            "Action is forbidden for this product.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult ValidationError(Guid productId, uint expectedVersion, string message) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.ValidationFailed,
            message,
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult DependencyBlocked(Guid productId, uint expectedVersion, string? message = null) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.DependencyBlocked,
            message ?? "A dependency is currently blocked.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult AuthoringFailure(Guid productId, uint expectedVersion, string message) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.AuthoringFailed,
            message,
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);

    public static BulkItemResult RequestCancelled(Guid productId, uint expectedVersion) =>
        new(
            productId,
            BulkItemStatus.Failed,
            BulkErrorCodes.RequestCancelled,
            "The request was cancelled before this item could be processed.",
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            null);
}

public sealed record BulkOperationResult(
    int Requested,
    int Accepted,
    int Processed,
    int Unchanged,
    int Failed,
    string CorrelationId,
    IReadOnlyList<BulkItemResult> Results)
{
    public static BulkOperationResult Empty(int requested, int accepted, string correlationId) =>
        new(requested, accepted, 0, 0, 0, correlationId, Array.Empty<BulkItemResult>());
}
