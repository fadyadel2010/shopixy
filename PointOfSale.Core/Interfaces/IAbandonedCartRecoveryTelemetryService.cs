namespace PointOfSale.Core.Interfaces;

public interface IAbandonedCartRecoveryTelemetryService
{
    Task<AbandonedCartRecoveryLogWriteResult> RecordWorkflowResultAsync(
        AbandonedCartRecoveryLogCommand command,
        CancellationToken cancellationToken = default);

    Task<AbandonedCartRecoveryStatsSnapshot> GetStatsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public sealed record AbandonedCartRecoveryLogCommand(
    Guid TenantId,
    Guid AbandonedCartId,
    bool FirstMessageSent,
    DateTime? FirstMessageSentAtUtc,
    string? FirstMessageError,
    bool SecondMessageSent,
    DateTime? SecondMessageSentAtUtc,
    string? SecondMessageError,
    DateTime? WorkflowCompletedAtUtc,
    string? IdempotencyKey);

public sealed record AbandonedCartRecoveryLogWriteResult(
    bool Success,
    bool IsDuplicate,
    bool NotFound,
    Guid? LogId,
    Guid? CartId,
    string? PhoneNumber)
{
    public static AbandonedCartRecoveryLogWriteResult Created(Guid logId, Guid cartId, string? phoneNumber) =>
        new(true, false, false, logId, cartId, phoneNumber);

    public static AbandonedCartRecoveryLogWriteResult Duplicate(Guid logId, Guid cartId, string? phoneNumber) =>
        new(true, true, false, logId, cartId, phoneNumber);

    public static AbandonedCartRecoveryLogWriteResult MissingCart() =>
        new(false, false, true, null, null, null);
}

public sealed record AbandonedCartRecoveryStatsSnapshot(
    int TotalAbandoned,
    int MessagesSent,
    int Recovered,
    decimal RecoveryRate);
