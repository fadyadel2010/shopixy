using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Online Customer.
/// Maps to 'online_customers' collection.
/// 
/// ARCHITECTURAL RULES:
/// - Read-only for Storefront
/// - Projected from PostgreSQL via events
/// - Contains only LIVE customers
/// - NO sensitive data (NO password hash)
/// - Unique index on (TenantId, CustomerId)
/// - TenantId is MANDATORY
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
[BsonIgnoreExtraElements]
public class OnlineCustomerDocument
{
    /// <summary>
    /// MongoDB document ID.
    /// Auto-generated if not provided.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// The tenant that owns this customer account.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Unique identifier of the customer (from PostgreSQL).
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer email address (normalized to lowercase).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Authentication provider: local, google, facebook
    /// </summary>
    public string Provider { get; set; } = "local";

    /// <summary>
    /// Customer's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Customer's phone number (optional).
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Whether the customer account is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the customer's email has been verified.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// UTC timestamp when the account was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the last successful login.
    /// </summary>
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when this document was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Last customer lifecycle/profile event applied to this projection.
    /// Prevents older events from reactivating or overwriting newer customer state.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// UTC timestamp of the last customer lifecycle/profile event applied.
    /// </summary>
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version tie-breaker for customer lifecycle/profile events with the same timestamp.
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Last customer-wide session revocation event applied.
    /// Session projections use this as a watermark so late token-issued events cannot
    /// recreate sessions that were already revoked in PostgreSQL.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastSessionRevocationEventId { get; set; }

    /// <summary>
    /// UTC timestamp of the last customer-wide session revocation event applied.
    /// </summary>
    public DateTime? LastSessionRevocationEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version tie-breaker for customer-wide session revocation events.
    /// </summary>
    public int LastSessionRevocationEventVersion { get; set; }

    /// <summary>
    /// Source revocation timestamp for all sessions that existed at or before this time.
    /// </summary>
    public DateTime? LastSessionRevokedAtUtc { get; set; }

    /// <summary>
    /// Source revocation reason for the session revocation watermark.
    /// </summary>
    public string? LastSessionRevocationReason { get; set; }

    /// <summary>
    /// Schema version for document migration.
    /// Version 2: Added session revocation watermark fields (LastSessionRevocationEventId, 
    /// LastSessionRevocationEventOccurredAtUtc, LastSessionRevocationEventVersion,
    /// LastSessionRevokedAtUtc, LastSessionRevocationReason).
    /// </summary>
    public int SchemaVersion { get; set; } = 2;
}
