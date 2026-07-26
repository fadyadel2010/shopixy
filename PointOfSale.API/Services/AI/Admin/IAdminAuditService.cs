using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Admin;

/// <summary>
/// Admin audit service for explaining system events.
/// PHASE 4: Audit & Explanation Intelligence.
/// 
/// This service helps admins understand:
/// - Why something happened
/// - Who/what triggered it
/// - Whether it was automatic or manual
/// 
/// RULES:
/// - Explain only, never suggest actions
/// - Neutral tone, no blame
/// - Human-readable, no technical jargon
/// </summary>
public interface IAdminAuditService
{
    /// <summary>
    /// Explain an audit event to an admin.
    /// </summary>
    /// <param name="context">Audit context with event details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Human-readable explanation</returns>
    Task<AdminExplanationResponse> ExplainEventAsync(
        AdminAuditContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build an audit context from an event.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="eventType">Event type</param>
    /// <param name="entityId">Related entity ID</param>
    /// <param name="question">Admin's question</param>
    Task<AdminAuditContext> BuildAuditContextAsync(
        Guid tenantId,
        string eventType,
        Guid entityId,
        string question);
}

/// <summary>
/// Response from admin explanation service.
/// </summary>
public class AdminExplanationResponse
{
    /// <summary>
    /// Human-readable explanation of what happened.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Whether the explanation was generated successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Summary of the actor (who triggered the event).
    /// </summary>
    public string? ActorSummary { get; set; }

    /// <summary>
    /// Whether this was a manual action.
    /// </summary>
    public bool WasManual { get; set; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTime? EventTimestamp { get; set; }
}
