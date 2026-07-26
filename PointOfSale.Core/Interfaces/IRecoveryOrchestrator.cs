namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Orchestrates abandoned cart recovery workflows via external automation platform (n8n).
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// RESPONSIBILITIES:
/// - Call n8n webhook with recovery payload
/// - Retry on failure (3 attempts)
/// - Timeout protection (5 seconds)
/// - Log all attempts
/// - Never throw exceptions
/// 
/// DOES NOT:
/// - Send WhatsApp messages directly
/// - Make eligibility decisions
/// - Store state (stateless)
/// </summary>
public interface IRecoveryOrchestrator
{
    /// <summary>
    /// Triggers recovery workflow via n8n webhook.
    /// 
    /// BEHAVIOR:
    /// - POST to n8n webhook endpoint
    /// - Retry 3 times on failure (exponential backoff)
    /// - Timeout after 5 seconds
    /// - Log all attempts
    /// - Returns true if webhook accepted (2xx response)
    /// - Returns false on failure (never throws)
    /// 
    /// IDEMPOTENCY:
    /// - n8n workflow must handle duplicate calls
    /// - Caller should check RecoveryStatus before calling
    /// </summary>
    Task<bool> TriggerRecoveryWorkflowAsync(
        Guid tenantId,
        Guid abandonedCartId,
        Guid cartId,
        string? sessionId,
        Guid? customerId,
        string? phoneNumber,
        string? email,
        string? contactChannel,
        string cartSnapshotJson,
        int firstMessageDelayMinutes,
        int secondMessageDelayMinutes,
        TimeSpan workingHoursFrom,
        TimeSpan workingHoursTo,
        string storeName,
        string storefrontBaseUrl,
        string recoveryLink,
        CancellationToken ct = default);
}
