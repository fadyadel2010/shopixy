using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Immutable message log document for audit and troubleshooting.
/// </summary>
public class WhatsAppMessageLogDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [BsonElement("tenantId")]
    public string TenantId { get; set; } = string.Empty;
    
    [BsonElement("accountId")]
    public string AccountId { get; set; } = string.Empty;
    
    [BsonElement("targetPhone")]
    public string TargetPhone { get; set; } = string.Empty;
    
    /// <summary>
    /// Masked phone for privacy (e.g., +20123****890)
    /// </summary>
    [BsonElement("maskedPhone")]
    public string MaskedPhone { get; set; } = string.Empty;
    
    [BsonElement("messageType")]
    public string MessageType { get; set; } = string.Empty; // ORDER_CONFIRMATION, OTP
    
    [BsonElement("relatedEntityId")]
    public string? RelatedEntityId { get; set; } // OrderId or CustomerId
    
    [BsonElement("relatedEntityType")]
    public string? RelatedEntityType { get; set; } // Order, OnlineCustomer
    
    /// <summary>
    /// Snapshot of message content (for debugging)
    /// </summary>
    [BsonElement("payloadSnapshot")]
    public BsonDocument? PayloadSnapshot { get; set; }
    
    [BsonElement("status")]
    public string Status { get; set; } = "PENDING"; // PENDING, DISPATCHING, SENT, FAILED, SKIPPED, DELIVERY_UNKNOWN
    
    [BsonElement("failureReason")]
    public string? FailureReason { get; set; }
    
    [BsonElement("sendAttempts")]
    public int SendAttempts { get; set; } = 0;
    
    [BsonElement("lastAttemptAtUtc")]
    public DateTime? LastAttemptAtUtc { get; set; }
    
    [BsonElement("sentAtUtc")]
    public DateTime? SentAtUtc { get; set; }

    [BsonElement("providerMessageId")]
    public string? ProviderMessageId { get; set; }

    [BsonElement("dispatchLeaseId")]
    public string? DispatchLeaseId { get; set; }

    [BsonElement("dispatchLeaseExpiresAtUtc")]
    public DateTime? DispatchLeaseExpiresAtUtc { get; set; }
    
    [BsonElement("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Idempotency key to prevent duplicate sends
    /// </summary>
    [BsonElement("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    [BsonElement("correlationId")]
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// Whether this message can be retried by the background retry job.
    /// OTP messages are NOT retryable because their content is not persisted (for security).
    /// Default is true for regular messages.
    /// </summary>
    [BsonElement("isRetryable")]
    public bool IsRetryable { get; set; } = true;
}
