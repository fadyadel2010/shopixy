using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Infrastructure.MongoDB.Serialization;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model document for categories.
/// This is the denormalized representation optimized for e-commerce storefront read queries.
/// 
/// Important:
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - CategoryId + TenantId forms the unique business key
/// - Supports hierarchical structure via ParentId
/// </summary>
public class CategoryDocument
{
    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this category.
    /// CRITICAL: This MUST match for tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL Category.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Category name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic category name for bilingual support
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// SEO-friendly URL slug
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Category description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Parent category ID for hierarchical structure.
    /// Null means this is a root category.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Display sort order within parent
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether the category is active in the system
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the category is visible on e-commerce storefront
    /// </summary>
    public bool IsOnlineVisible { get; set; } = true;

    /// <summary>
    /// When the category was created in PostgreSQL (source of truth)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this document was last synced/updated from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }
    
    #region Promotions Engine - Category Banner Fields
    
    /// <summary>
    /// Whether this category has an active promotion (offer).
    /// Updated by PromotionProjectionConsumer when category-scoped promotions change.
    /// </summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Banner text to display at top of category page (e.g., "Up to 30% OFF All Electronics")
    /// </summary>
    public string? PromotionBannerText { get; set; }
    
    /// <summary>
    /// ID of the active promotion for this category
    /// </summary>
    [BsonElement("ActivePromotionId")]
    [BsonSerializer(typeof(NullableGuidStringSerializer))]
    public Guid? ActivePromotionId { get; set; }
    
    /// <summary>
    /// End date of the active promotion for countdown timer
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PromotionEndDate { get; set; }
    
    /// <summary>
    /// Banner display style: gradient, solid, minimal, festive
    /// </summary>
    public string? PromotionBannerStyle { get; set; }
    
    #endregion
}
