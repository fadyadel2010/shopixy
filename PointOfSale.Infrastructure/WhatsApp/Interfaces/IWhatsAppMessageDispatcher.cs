using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.WhatsApp.Interfaces;

public interface IWhatsAppMessageDispatcher
{
    Task DispatchAsync(string messageLogId, CancellationToken ct = default);
    
    /// <summary>
    /// Dispatches a message with explicit message text (used for OTP where we don't persist the actual code).
    /// </summary>
    Task DispatchAsync(string messageLogId, string messageText, CancellationToken ct = default);
}

public interface IWhatsAppMessageLogService
{
    Task<WhatsAppMessageLogDocument> CreateLogAsync(WhatsAppMessageLogDocument log);
    Task UpdateStatusAsync(string id, string status, string? failureReason = null);
    Task<WhatsAppMessageLogDocument?> GetByIdAsync(string id);
    Task<WhatsAppMessageLogDocument?> TryClaimDispatchAsync(
        string id,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        int maxAttempts,
        CancellationToken ct = default);
    Task<bool> CompleteDispatchAsync(
        string id,
        string tenantId,
        string accountId,
        string leaseId,
        string status,
        string? failureReason = null,
        string? providerMessageId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets pending messages for retry (status = QUEUED or FAILED with attempts &lt; maxRetries).
    /// </summary>
    Task<List<WhatsAppMessageLogDocument>> GetPendingMessagesAsync(int maxRetries, int limit, CancellationToken ct = default);
    
    /// <summary>
    /// Gets message logs for a tenant with optional filtering.
    /// </summary>
    Task<(List<WhatsAppMessageLogDocument> Items, long TotalCount)> GetLogsAsync(
        Guid tenantId, 
        int page, 
        int pageSize, 
        string? status = null, 
        string? messageType = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets analytics summary for a tenant.
    /// </summary>
    Task<WhatsAppMessageAnalytics> GetAnalyticsAsync(
        Guid tenantId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}

public class WhatsAppMessageAnalytics
{
    public long TotalMessagesSent { get; set; }
    public long TotalMessagesFailed { get; set; }
    public long TotalMessagesPending { get; set; }
    public double SuccessRate { get; set; }
    public Dictionary<string, long> MessagesByType { get; set; } = new();
    public Dictionary<string, long> MessagesByStatus { get; set; } = new();
}
