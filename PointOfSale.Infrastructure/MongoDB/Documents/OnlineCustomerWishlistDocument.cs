using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Online Customer Wishlist.
/// Maps to 'online_customer_wishlists' collection.
/// 
/// ARCHITECTURAL RULES:
/// - Read-only for Storefront
/// - Projected from PostgreSQL via events
/// - One document per customer (contains array of items)
/// - NO product details stored – only ProductId
/// - Product details fetched from catalog at read time
/// - TenantId is MANDATORY
/// 
/// COLLECTION STRUCTURE:
/// - Each customer has ONE document with an array of wishlist items
/// - Items contain only: productId, addedAtUtc
/// - totalCount is denormalized for badge display
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
[BsonIgnoreExtraElements]
public class OnlineCustomerWishlistDocument
{
    /// <summary>
    /// MongoDB document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this wishlist.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// ID of the customer who owns this wishlist.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// List of wishlist items (product IDs with timestamps).
    /// </summary>
    public List<WishlistItemEntry> Items { get; set; } = new();

    /// <summary>
    /// Denormalized count for badge display.
    /// Updated on add/remove operations.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// UTC timestamp when this document was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastClearEventId { get; set; }

    public DateTime? LastClearEventOccurredAtUtc { get; set; }

    public int LastClearEventVersion { get; set; }

    public DateTime? LastClearedAtUtc { get; set; }

    /// <summary>
    /// Schema version for document migration.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>
/// Embedded document for a single wishlist item.
/// Minimal data – product details fetched separately.
/// </summary>
[BsonIgnoreExtraElements]
public class WishlistItemEntry
{
    /// <summary>
    /// ID of the product in the wishlist.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    /// <summary>
    /// UTC timestamp when the item was added.
    /// </summary>
    public DateTime AddedAtUtc { get; set; }

    public bool IsRemoved { get; set; }

    public DateTime? RemovedAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    public DateTime? LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }
}
