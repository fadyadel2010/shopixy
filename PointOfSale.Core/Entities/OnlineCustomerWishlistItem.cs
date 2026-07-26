using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Online Customer Wishlist Item entity.
/// 
/// ARCHITECTURAL RULES:
/// - Wishlist belongs to OnlineCustomer, NOT POS Customer
/// - PostgreSQL is the Write Model (source of truth)
/// - MongoDB is the Read Model (projected via events)
/// - Soft delete only – never hard delete
/// - TenantId is MANDATORY everywhere
/// - NO product data duplication – only ProductId reference
/// 
/// UNIQUE CONSTRAINT:
/// - (TenantId, OnlineCustomerId, ProductId) enforces one item per product per customer
/// - Adding same product twice is idempotent (no error, no duplicate)
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistItem : ITenantOwned
{
    /// <summary>
    /// Unique identifier for the wishlist item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant that owns this wishlist item.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The online customer who owns this wishlist item.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }

    /// <summary>
    /// Reference to the product in the wishlist.
    /// NO join with Product table – only store the ID.
    /// Product details are fetched from catalog at read time.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// UTC timestamp when the item was added to wishlist.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft delete flag.
    /// When true, the item is considered removed from wishlist.
    /// </summary>
    public bool IsRemoved { get; set; } = false;

    /// <summary>
    /// UTC timestamp when the item was removed (soft deleted).
    /// NULL if not removed.
    /// </summary>
    public DateTime? RemovedAtUtc { get; set; }
}
