namespace PointOfSale.API.Services.BackgroundTasks;

/// <summary>
/// Represents a work item for background RedirectUrl recovery from Stripe.
/// </summary>
public sealed class RedirectUrlRecoveryWorkItem
{
    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    public int AttemptCount { get; init; } = 0;
    public DateTime? LastAttemptAt { get; init; }
}
