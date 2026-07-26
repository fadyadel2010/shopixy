namespace PointOfSale.Core.Events;

#region Wishlist Events

/// <summary>
/// Event raised when a product is added to a customer's wishlist.
/// 
/// ARCHITECTURAL RULES:
/// - Contains: TenantId, CustomerId, ProductId
/// - NO product data duplication
/// - Idempotent-safe (adding same product twice is OK)
/// - Used for MongoDB projection & cache invalidation
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistItemAddedEvent : IntegrationEvent
{
    /// <summary>
    /// Unique identifier of the wishlist item.
    /// </summary>
    public Guid WishlistItemId { get; set; }

    /// <summary>
    /// ID of the customer who added the item.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// ID of the product added to wishlist.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// UTC timestamp when the item was added.
    /// </summary>
    public DateTime AddedAtUtc { get; set; }
}

/// <summary>
/// Event raised when a product is removed from a customer's wishlist.
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistItemRemovedEvent : IntegrationEvent
{
    /// <summary>
    /// ID of the customer who removed the item.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// ID of the product removed from wishlist.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// UTC timestamp when the item was removed.
    /// </summary>
    public DateTime RemovedAtUtc { get; set; }
}

/// <summary>
/// Event raised when a customer's entire wishlist is cleared.
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistClearedEvent : IntegrationEvent
{
    /// <summary>
    /// ID of the customer whose wishlist was cleared.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Number of items that were cleared.
    /// </summary>
    public int ItemsCleared { get; set; }

    /// <summary>
    /// UTC timestamp when the wishlist was cleared.
    /// </summary>
    public DateTime ClearedAtUtc { get; set; }
}

#endregion
