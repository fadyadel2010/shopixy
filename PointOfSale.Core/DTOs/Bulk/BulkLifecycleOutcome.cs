namespace PointOfSale.Core.DTOs.Bulk;

public enum BulkLifecycleOutcomeKind
{
    Success,
    RequestValidationFailed,
    IdempotencyConflict,
    IdempotencyInProgress,
    Failed
}

public sealed record BulkLifecycleOutcome(
    BulkLifecycleOutcomeKind Kind,
    BulkOperationResult? OperationResult,
    IReadOnlyList<BulkRequestValidationError>? ValidationErrors,
    string? Message)
{
    public static BulkLifecycleOutcome Success(BulkOperationResult result) =>
        new(BulkLifecycleOutcomeKind.Success, result, null, null);

    public static BulkLifecycleOutcome RequestValidationFailed(
        IReadOnlyList<BulkRequestValidationError> errors) =>
        new(BulkLifecycleOutcomeKind.RequestValidationFailed, null, errors, null);

    public static BulkLifecycleOutcome IdempotencyConflict(string message) =>
        new(BulkLifecycleOutcomeKind.IdempotencyConflict, null, null, message);

    public static BulkLifecycleOutcome IdempotencyInProgress(string message) =>
        new(BulkLifecycleOutcomeKind.IdempotencyInProgress, null, null, message);

    public static BulkLifecycleOutcome Failed(string message) =>
        new(BulkLifecycleOutcomeKind.Failed, null, null, message);
}
