namespace PointOfSale.Core.DTOs.Bulk;

public static class BulkErrorCodes
{
    public const string BulkRequestRequired = "BULK_REQUEST_REQUIRED";
    public const string ProductsRequired = "PRODUCT_BULK_ITEMS_REQUIRED";
    public const string BatchTooLarge = "PRODUCT_BULK_BATCH_TOO_LARGE";
    public const string InvalidProductId = "INVALID_PRODUCT_ID";
    public const string InvalidExpectedVersion = "INVALID_EXPECTED_VERSION";
    public const string DuplicateProductConflict = "DUPLICATE_PRODUCT_CONFLICT";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyTooLong = "IDEMPOTENCY_KEY_TOO_LONG";

    public const string NotFound = "NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string Forbidden = "FORBIDDEN";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string DependencyBlocked = "DEPENDENCY_BLOCKED";
    public const string AlreadyActive = "ALREADY_ACTIVE";
    public const string AlreadyInactive = "ALREADY_INACTIVE";
    public const string AlreadyPublished = "ALREADY_PUBLISHED";
    public const string AlreadyUnpublished = "ALREADY_UNPUBLISHED";
    public const string ProductInactive = "PRODUCT_INACTIVE";
    public const string PublicationNotReady = "PUBLICATION_NOT_READY";
    public const string AuthoringFailed = "AUTHORING_FAILED";
    public const string RequestCancelled = "REQUEST_CANCELLED";
}
