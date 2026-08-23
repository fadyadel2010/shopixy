using PointOfSale.Core.Entities;

namespace PointOfSale.Core.DTOs.Bulk;

public sealed record BulkIdempotencyKeyValidationResult(bool IsValid, string? ErrorCode, string? Message)
{
    public static BulkIdempotencyKeyValidationResult Valid() => new(true, null, null);

    public static BulkIdempotencyKeyValidationResult Invalid(string errorCode, string message) =>
        new(false, errorCode, message);
}

public enum BulkIdempotencyReplayKind
{
    NotFound,
    Completed,
    Conflict,
    InProgress
}

public sealed record BulkIdempotencyReplay(
    BulkIdempotencyReplayKind Kind,
    ProductRequestIdempotency? Record);

public sealed record BulkIdempotencyClaim(
    ProductRequestIdempotency? Record,
    BulkIdempotencyReplay? Replay,
    bool Recovered = false);

public sealed record BulkIdempotencyResolution(
    BulkIdempotencyReplayKind Kind,
    ProductRequestIdempotency? Record);
