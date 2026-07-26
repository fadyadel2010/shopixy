using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model document for payment methods.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// 
/// ARCHITECTURE:
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - Projected via PaymentMethodProjectionConsumer
/// - ❌ NO SECRETS - ConfigEncrypted is NEVER stored here
/// </summary>
public class PaymentMethodDocument
{
    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this payment method.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL TenantPaymentMethod.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Unique code identifying the payment method type.
    /// Examples: "COD", "STRIPE", "PAYMOB"
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name (English).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Display name (Arabic).
    /// </summary>
    public string? DisplayNameAr { get; set; }

    /// <summary>
    /// Description for customers.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Arabic description for customers.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Whether this payment method is enabled for customers.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Tombstone flag. Deleted payment methods stay as inactive tombstones so
    /// stale earlier events cannot resurrect customer-visible payment options.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Display order in payment method selection.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this method supports online payments.
    /// </summary>
    public bool SupportsOnline { get; set; }

    /// <summary>
    /// Whether this method supports offline payments.
    /// </summary>
    public bool SupportsOffline { get; set; }

    /// <summary>
    /// Whether this is in sandbox/test mode.
    /// </summary>
    public bool IsSandbox { get; set; }

    /// <summary>
    /// Icon identifier for display.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Minimum order amount for this payment method.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? MinOrderAmount { get; set; }

    /// <summary>
    /// Maximum order amount for this payment method.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? MaxOrderAmount { get; set; }

    /// <summary>
    /// Additional fee for using this payment method.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Fee { get; set; }

    /// <summary>
    /// Fee type: "fixed" or "percentage"
    /// </summary>
    public string FeeType { get; set; } = "fixed";

    /// <summary>
    /// Whether the payment method has valid configuration for storefront exposure.
    /// </summary>
    [BsonIgnoreIfNull]
    public bool? IsConfigurationValid { get; set; }

    /// <summary>
    /// Legacy compatibility field retained for older projected documents.
    /// New code should use IsConfigurationValid.
    /// </summary>
    public bool HasConfiguration { get; set; }

    /// <summary>
    /// Storefront-safe, non-secret configuration values projected for public reads.
    /// Example: MANUALWALLET wallet number.
    /// </summary>
    public Dictionary<string, string> PublicConfiguration { get; set; } = new();

    /// <summary>
    /// When this document was last synced/updated from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// The EventId that last updated this document (for debugging/tracing)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Timestamp of the newest event applied to this projection.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version of the last event applied to this projection.
    /// Used as a deterministic tie-breaker when LastEventOccurredAtUtc is equal.
    /// </summary>
    public int LastEventVersion { get; set; }
}
