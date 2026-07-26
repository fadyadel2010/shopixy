namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks all recovery workflow executions for analytics and debugging.
/// One record per n8n workflow run.
/// Phase: Abandoned Cart Hardening
/// 
/// PURPOSE:
/// - Centralized logging of n8n workflow results
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
    
    // Second message tracking
    public bool SecondMessageSent { get; set; } = false;
    public DateTime? SecondMessageSentAtUtc { get; set; }
    public string? SecondMessageError { get; set; }
    
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
