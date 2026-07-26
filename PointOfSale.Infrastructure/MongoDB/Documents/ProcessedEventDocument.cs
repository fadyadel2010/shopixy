using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Idempotency inbox for bulk/fan-out events.
/// Prevents duplicate processing of events that trigger expensive operations
/// like tenant-wide updates or full Elasticsearch reindexing.
/// 
/// TTL: 30 days (events older than this are automatically deleted)
/// </summary>
public class ProcessedEventDocument
{
    private string _consumerName = string.Empty;

    /// <summary>
    /// Composite key: {ConsumerName}:{EventId}
    /// Makes the inbox consumer-specific to prevent conflicts when multiple consumers
    /// process the same integration event.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original event ID (stored separately for querying/debugging)
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event type for debugging/monitoring (e.g., "TenantCurrencyChangedEvent")
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Consumer that processed this event (e.g., "AffiliateProductPolicyProjectionConsumer").
    /// Must be a valid C# identifier without the ':' delimiter character to ensure
    /// the composite Id remains parseable.
    /// </summary>
    public string ConsumerName
    {
        get => _consumerName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "ConsumerName cannot be null or empty.",
                    nameof(ConsumerName));
            }

            if (value.Contains(':'))
            {
                throw new ArgumentException(
                    "ConsumerName cannot contain ':' character as it is used as the delimiter in the composite Id. " +
                    "Use a valid C# identifier (e.g., class name via nameof()).",
                    nameof(ConsumerName));
            }
            _consumerName = value;
        }
    }

    /// <summary>
    /// Processing status: "InProgress", "Processed", "Failed"
    /// Used for atomic reservation to prevent race conditions under parallel redelivery.
    /// </summary>
    public string Status { get; set; } = "Processed";

    /// <summary>
    /// When the event was claimed/started processing
    /// </summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>
    /// When the event was processed
    /// </summary>
    public DateTime ProcessedAtUtc { get; set; }

    /// <summary>
    /// Optional tenant context for filtering/debugging
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Instance/worker that claimed this event (for debugging stuck events)
    /// </summary>
    public string? Owner { get; set; }
}
