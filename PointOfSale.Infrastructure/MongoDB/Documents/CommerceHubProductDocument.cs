using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

[BsonIgnoreExtraElements]
public class CommerceHubProductDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.CommerceHubProductMongoSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.CommerceHubProductMongoProjectionVersion;

    public const string SearchSyncPending = "Pending";
    public const string SearchSyncSynced = "Synced";
    public const string SearchSyncFailed = "Failed";
    public const string SearchSyncTerminalFailure = "TerminalFailure";
    
    public const int MaxSearchRetries = 5;

    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.CommerceHubProduct;

    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    public string StoreName { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Image { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? CommerceHubPriceOverride { get; set; }

    public string PriceSource { get; set; } = "ProductVariantEffectiveOnlinePrice";

    public bool OverridePresent { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CategoryId { get; set; }

    public string? CategoryName { get; set; }
    public string? CategoryPath { get; set; }

    public string Currency { get; set; } = "EGP";

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal StoreShippingFee { get; set; }

    public bool InStock { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal AvailableStock { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? StockUpdatedAtUtc { get; set; }

    public int LastStockEventVersion { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public double PopularityScore { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastClickAtUtc { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>
    /// Dynamic category attributes stored as JSON string (backward-compatible).
    /// </summary>
    public string? AttributesJson { get; set; }

    /// <summary>
    /// Typed flat attribute map for ES-ready reads.
    /// Strings stored as keyword (lowercase), numbers as double.
    /// Structure: { "brand": "samsung", "screen_size_inches": 55 }
    /// No nesting. No arrays. Flat key-value only.
    /// </summary>
    [BsonIgnoreIfNull]
    public BsonDocument? Attributes { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Freshness marker for listing publication events. Kept separate from LastEventId
    /// because stock, price, search, and store metadata events can update the same document.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastListingEventOccurredAtUtc { get; set; }

    public int LastListingEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastProductProjectionEventOccurredAtUtc { get; set; }

    public int LastProductProjectionEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastProductLifecycleEventOccurredAtUtc { get; set; }

    public int LastProductLifecycleEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastStoreMetadataEventOccurredAtUtc { get; set; }

    public int LastStoreMetadataEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastCurrencyEventOccurredAtUtc { get; set; }

    public int LastCurrencyEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? LastShippingEventOccurredAtUtc { get; set; }

    public int LastShippingEventVersion { get; set; }

    /// <summary>
    /// Timestamp of the last price event applied to this listing.
    /// Used to prevent out-of-order price events from overwriting newer data.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastPriceEventTimestamp { get; set; }

    public int LastPriceEventVersion { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SearchLastAttemptedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SearchLastSucceededAtUtc { get; set; }

    public string? SearchLastError { get; set; }

    public string SearchSyncStatus { get; set; } = SearchSyncPending;
    
    public int SearchRetryCount { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSyncedAt { get; set; }
}
