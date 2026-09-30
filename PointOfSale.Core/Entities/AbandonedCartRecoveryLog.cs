namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable schedule and audit record for an abandoned-cart recovery workflow.
/// 
/// PURPOSE:
/// - Durable first/second message scheduling
/// - Analytics for recovery success rates
/// - Debugging failed recoveries
/// - Audit trail for customer communications
/// </summary>
public class AbandonedCartRecoveryLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid TenantId { get; set; }
    
    public Guid AbandonedCartId { get; set; }
    
    public Guid CartId { get; set; }
    
    public string PhoneNumber { get; set; } = string.Empty;
    
    // First message tracking
    public bool FirstMessageSent { get; set; } = false;
    public DateTime? FirstMessageSentAtUtc { get; set; }
    public string? FirstMessageError { get; set; }
    public DateTime? FirstMessageDueAtUtc { get; set; }
    public string FirstMessageStatus { get; set; } = AbandonedCartMessageStatus.Scheduled;
    public int FirstMessageAttempts { get; set; }
    public string? FirstMessageLeaseId { get; set; }
    public DateTime? FirstMessageLeaseExpiresAtUtc { get; set; }
    public string? FirstMessageDispatchLogId { get; set; }
    
    // Second message tracking
    public bool SecondMessageSent { get; set; } = false;
    public DateTime? SecondMessageSentAtUtc { get; set; }
    public string? SecondMessageError { get; set; }
    public DateTime? SecondMessageDueAtUtc { get; set; }
    public string SecondMessageStatus { get; set; } = AbandonedCartMessageStatus.NotScheduled;
    public int SecondMessageAttempts { get; set; }
    public string? SecondMessageLeaseId { get; set; }
    public DateTime? SecondMessageLeaseExpiresAtUtc { get; set; }
    public string? SecondMessageDispatchLogId { get; set; }

    /// <summary>
    /// Tenant-authoritative IANA time zone used when the schedule was last evaluated.
    /// Current settings are still reloaded before every dispatch.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
    
    // Workflow tracking
    public DateTime WorkflowStartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? WorkflowCompletedAtUtc { get; set; }
    
    /// <summary>
    /// Idempotency key for preventing duplicate log entries.
    /// Format: {AbandonedCartId}:{WorkflowExecutionId}
    /// </summary>
    public string? IdempotencyKey { get; set; }
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public AbandonedCart? AbandonedCart { get; set; }
}

public static class AbandonedCartMessageStatus
{
    public const string NotScheduled = "NotScheduled";
    public const string Scheduled = "Scheduled";
    public const string Dispatching = "Dispatching";
    public const string Sent = "Sent";
    public const string RetryScheduled = "RetryScheduled";
    public const string Suppressed = "Suppressed";
    public const string DeliveryUnknown = "DeliveryUnknown";
    public const string Disabled = "Disabled";
    public const string Failed = "Failed";
}
