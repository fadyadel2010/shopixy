using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Serialization;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// Embedded document for product unit pricing.
/// Contains the selling price for a specific unit of measure.
/// </summary>
public class ProductUnitDocument
{
    /// <summary>
    /// ProductUnit ID from PostgreSQL
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid UnitId { get; set; }

    /// <summary>
    /// Unit name (e.g., "Piece", "Carton", "Box")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unit symbol (e.g., "pc", "ctn", "bx")
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Selling price for this unit.
    /// THIS IS THE CORRECT PRICE SOURCE FOR STOREFRONT.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal SalePrice { get; set; }

    /// <summary>
    /// Conversion factor relative to base unit.
    /// Base unit has factor = 1.
    /// Example: If base is "Piece" and this is "Carton" with 12 pieces, factor = 12.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal ConversionFactor { get; set; } = 1;

    /// <summary>
    /// Whether this is the base unit (ConversionFactor = 1)
    /// </summary>
    public bool IsBaseUnit { get; set; }
}

public class ProductMediaDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public int Position { get; set; }
}

public class ProductFaqDocument
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// MongoDB read model document for products.
/// This is the denormalized representation optimized for e-commerce read queries.
/// 
/// Important:
/// - This is NOT the source of truth (PostgreSQL is)
/// - Documents are tenant-isolated by TenantId
/// - ProductId + TenantId forms the unique business key
/// 
/// CRITICAL PRICING RULE:
/// - DefaultUnit.SalePrice is the ONLY valid selling price for Storefront
/// - Price field is LEGACY and should not be used for cart/checkout
/// - If DefaultUnit is null, product is NOT purchasable online
/// </summary>
[BsonIgnoreExtraElements]
public class ProductDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.StorefrontProductMongoSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.StorefrontProductMongoProjectionVersion;

    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// Stable read model identity used by the evolution framework.
    /// </summary>
    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.StorefrontProduct;

    /// <summary>
    /// Shape version of this Mongo document. New runtime-required fields are only strict after convergence.
    /// </summary>
    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    /// <summary>
    /// Version of the projection logic that wrote this document.
    /// </summary>
    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    /// <summary>
    /// Set by convergence certification once all documents for this read model are safely upgraded.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    /// <summary>
    /// The tenant that owns this product.
    /// CRITICAL: This MUST match the event's TenantId for tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL Product.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Product name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic name for bilingual support (nullable for MVP)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Stock Keeping Unit - unique product identifier
    /// </summary>
    public string? SKU { get; set; }

    /// <summary>
    /// Barcode for scanning
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// POS Sale price from Products.SalePrice.
    /// Used as fallback if OnlinePrice is not set.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    /// <summary>
    /// Online/Storefront price for e-commerce display.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// If null or 0, falls back to Price.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? OnlinePrice { get; set; }
    
    /// <summary>
    /// Computed effective price for storefront: OnlinePrice if set, otherwise Price.
    /// </summary>
    [BsonIgnore]
    public decimal EffectivePrice => (OnlinePrice.HasValue && OnlinePrice.Value > 0) ? OnlinePrice.Value : Price;

    /// <summary>
    /// Storefront-facing effective price for simple products.
    /// If DefaultUnit is missing, the product is not purchasable online.
    /// </summary>
    [BsonIgnore]
    public decimal StorefrontEffectivePrice => ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice(this);

    /// <summary>
    /// Visible storefront price across product shapes.
    /// Variant products use the default variant; simple products use the default unit.
    /// </summary>
    [BsonIgnore]
    public decimal StorefrontVisiblePrice => ProductStorefrontVisibilityAuthority.ResolveVisiblePrice(this);

    public bool TryResolveAffiliatePublicPricing(out AffiliatePublicPricingSnapshot snapshot)
        => ProductStorefrontVisibilityAuthority.TryResolveAffiliatePublicPricing(this, out snapshot);

    public bool TryResolveAffiliateDiscoveryBasePrice(out decimal basePrice)
        => ProductStorefrontVisibilityAuthority.TryResolveAffiliateDiscoveryBasePrice(this, out basePrice);

    /// <summary>
    /// Default selling unit for this product (optional).
    /// Used for unit-based POS operations.
    /// </summary>
    public ProductUnitDocument? DefaultUnit { get; set; }

    /// <summary>
    /// All available units for this product.
    /// Used for POS multi-unit support.
    /// </summary>
    public List<ProductUnitDocument> Units { get; set; } = new();

    /// <summary>
    /// Product purchase/cost price
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal PurchasePrice { get; set; }

    /// <summary>
    /// Category reference from PostgreSQL
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Denormalized category name for display
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// Product description for detail page
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Arabic product description for localized marketplace surfaces.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// SEO title preserved from the POS write model.
    /// </summary>
    public string? MetaTitle { get; set; }

    /// <summary>
    /// SEO description preserved from the POS write model.
    /// </summary>
    public string? MetaDescription { get; set; }

    /// <summary>
    /// Structured feature list for marketplace detail experiences.
    /// </summary>
    public List<string> Features { get; set; } = new();

    /// <summary>
    /// Structured FAQ list for marketplace detail experiences.
    /// </summary>
    public List<ProductFaqDocument> Faqs { get; set; } = new();

    /// <summary>
    /// Structured tag list for marketplace discovery and display.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Indicates whether the rich product content originated from AI generation.
    /// </summary>
    public bool AiGenerated { get; set; }

    /// <summary>
    /// Optional AI metadata stored as a BSON document.
    /// </summary>
    [BsonIgnoreIfNull]
    public BsonDocument? AiMetadata { get; set; }

    /// <summary>
    /// Brand name for filtering and display.
    /// Used for faceted navigation in product discovery.
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// Product images (JSON or comma-separated URLs)
    /// </summary>
    public string? Images { get; set; }

    /// <summary>
    /// Ordered product media gallery.
    /// This is the forward-looking storefront media model.
    /// If empty, legacy Images remains the compatibility fallback.
    /// </summary>
    public List<ProductMediaDocument> Media { get; set; } = new();

    /// <summary>
    /// Event id that last changed the media gallery projection.
    /// Kept separate from product-level ordering so media changes cannot block
    /// price, stock, or lifecycle projections.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastMediaEventId { get; set; }

    /// <summary>
    /// Timestamp of the newest media-gallery event applied to this document.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastMediaEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Product aggregate version carried by the newest media-gallery event.
    /// </summary>
    public int LastMediaEventVersion { get; set; }

    /// <summary>
    /// SEO-friendly URL slug
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Optional storefront product template identifier.
    /// Null means the default template should be used.
    /// </summary>
    public string? ProductTemplate { get; set; }

    /// <summary>
    /// Whether product is visible on e-commerce storefront
    /// </summary>
    public bool IsOnlineVisible { get; set; }

    /// <summary>
    /// Whether product is eligible for storefront search and discovery.
    /// SQL Products.IsSearchable owns this field; projections must not default it.
    /// </summary>
    public bool IsSearchable { get; set; } = true;

    /// <summary>
    /// Whether the product is active in the system
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the product should be featured on homepage.
    /// Featured products appear in the "Featured Products" homepage section.
    /// </summary>
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Whether to track inventory for this product.
    /// If false, stock validation is skipped during checkout.
    /// </summary>
    public bool TrackStock { get; set; } = true;

    /// <summary>
    /// Whether to allow backorders for this product.
    /// If true, product can be purchased even when out of stock.
    /// </summary>
    public bool AllowBackorder { get; set; }

    /// <summary>
    /// Total available stock quantity across all warehouses.
    /// Updated when stock changes via events.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Computed property: Whether the product is in stock.
    /// True if: !TrackStock || AllowBackorder || StockQuantity > 0
    /// </summary>
    [BsonIgnore]
    public bool InStock => !TrackStock || AllowBackorder || StockQuantity > 0;

    /// <summary>
    /// When the product was created in PostgreSQL (source of truth)
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this document was last synced/updated from an event
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>
    /// The EventId that last updated this document (for debugging/tracing)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Timestamp of the newest product-level event applied to this document.
    /// Used to reject out-of-order create/update events.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Version of the newest product-level event applied to this document.
    /// Acts as a deterministic tie-breaker when event timestamps match.
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Canonical PostgreSQL Product.ProjectionMutationVersion for the entire product aggregate.
    /// All downstream search mutations use this value rather than stream-local event versions.
    /// </summary>
    public long ProductGeneration { get; set; }

    /// <summary>
    /// Event id of the newest authoritative stock event applied to product-level stock fields.
    /// Kept separate from product lifecycle ordering so variant stock cannot be rejected
    /// by an unrelated product update event.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastStockEventId { get; set; }

    /// <summary>
    /// Source-of-truth timestamp of the newest stock event applied to product-level stock fields.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastStockUpdatedAtUtc { get; set; }

    /// <summary>
    /// Version of the newest stock event applied to product-level stock fields.
    /// </summary>
    public int LastStockEventVersion { get; set; }

    /// <summary>
    /// Product variants for storefront variant selection.
    ///
    /// PHASE 11.3 — STOREFRONT VARIANT SELECTION
    ///
    /// Every product MUST have at least one variant (the default variant).
    /// Simple products have a single variant with IsDefault = true.
    /// Variable products (e.g., T-Shirts with sizes) have multiple variants.
    ///
    /// CRITICAL:
    /// - Cart and Checkout use VariantId (not ProductId) for pricing
    /// - Price displayed on storefront comes from selected variant
    /// - Stock is per-variant, not per-product
    /// </summary>
    public List<ProductVariantDocument> Variants { get; set; } = new();

    /// <summary>
    /// Delete ordering metadata for variants removed from Variants[].
    /// Prevents delayed create/update events from resurrecting deleted variants.
    /// </summary>
    public List<ProductVariantDeletionTombstoneDocument> DeletedVariantTombstones { get; set; } = new();

    /// <summary>
    /// Maximum number of deletion tombstones to retain per product.
    /// Prevents unbounded growth of the tombstone list.
    /// </summary>
    public const int MAX_TOMBSTONES = 100;

    /// <summary>
    /// Adds or updates a deletion tombstone for a variant, ensuring bounded retention.
    ///
    /// Rules:
    /// - Deduplicates by VariantId (keeps newest timestamp)
    /// - Caps list size to MAX_TOMBSTONES
    /// - Removes oldest tombstones when limit is exceeded
    ///
    /// This prevents unbounded growth while maintaining delete ordering guarantees.
    ///
    /// NOTE: This method is for in-memory document manipulation.
    /// For MongoDB update operations, use BuildAddTombstoneUpdate() instead.
    /// </summary>
    /// <param name="tombstone">The tombstone to add or update</param>
    public void AddDeletedVariantTombstone(ProductVariantDeletionTombstoneDocument tombstone)
    {
        if (tombstone == null)
            throw new ArgumentNullException(nameof(tombstone));

        // Remove existing tombstone for this VariantId (deduplication)
        DeletedVariantTombstones.RemoveAll(t => t.VariantId == tombstone.VariantId);

        // Add the new tombstone
        DeletedVariantTombstones.Add(tombstone);

        // Trim to max size if exceeded, removing oldest tombstones
        if (DeletedVariantTombstones.Count > MAX_TOMBSTONES)
        {
            // Sort by timestamp ascending (oldest first)
            var sorted = DeletedVariantTombstones
                .OrderBy(t => t.LastEventOccurredAtUtc)
                .ThenBy(t => t.LastEventVersion)
                .ToList();

            // Keep only the newest MAX_TOMBSTONES entries
            DeletedVariantTombstones = sorted
                .Skip(sorted.Count - MAX_TOMBSTONES)
                .ToList();
        }
    }

    /// <summary>
    /// Builds a MongoDB update definition that adds a tombstone with bounded retention.
    ///
    /// This performs:
    /// 1. Pull existing tombstone for the same VariantId (deduplication)
    /// 2. Push the new tombstone
    /// 3. If list exceeds MAX_TOMBSTONES, trim to keep only newest entries
    ///
    /// The trimming is done via a pipeline update that sorts and slices the array.
    /// </summary>
    /// <param name="tombstone">The tombstone to add</param>
    /// <returns>Update definition for MongoDB</returns>
    public static UpdateDefinition<ProductDocument> BuildAddTombstoneUpdate(
        ProductVariantDeletionTombstoneDocument tombstone)
    {
        if (tombstone == null)
            throw new ArgumentNullException(nameof(tombstone));

        // Use a pipeline update to:
        // 1. Remove existing tombstone for this VariantId (deduplication)
        // 2. Add the new tombstone
        // 3. Sort by timestamp descending (newest first)
        // 4. Slice to keep only the newest MAX_TOMBSTONES entries
        var pipeline = new[]
        {
            new BsonDocument("$set", new BsonDocument
            {
                {
                    "DeletedVariantTombstones", new BsonDocument("$concatArrays", new BsonArray
                    {
                        // Filter out existing tombstone for this VariantId
                        new BsonDocument("$filter", new BsonDocument
                        {
                            { "input", new BsonDocument("$ifNull", new BsonArray 
                                { 
                                    "$DeletedVariantTombstones", 
                                    new BsonArray() 
                                }) 
                            },
                            { "as", "t" },
                            { "cond", new BsonDocument("$ne", new BsonArray
                                {
                                    "$$t.VariantId",
                                    tombstone.VariantId.ToString()
                                })
                            }
                        }),
                        // Add the new tombstone
                        new BsonArray
                        {
                            new BsonDocument
                            {
                                { "VariantId", tombstone.VariantId.ToString() },
                                { "LastEventOccurredAtUtc", tombstone.LastEventOccurredAtUtc },
                                { "LastEventVersion", tombstone.LastEventVersion }
                            }
                        }
                    })
                }
            }),
            // Sort by timestamp descending (newest first), then by version descending
            new BsonDocument("$set", new BsonDocument
            {
                {
                    "DeletedVariantTombstones", new BsonDocument("$slice", new BsonArray
                    {
                        new BsonDocument("$sortArray", new BsonDocument
                        {
                            { "input", "$DeletedVariantTombstones" },
                            { "sortBy", new BsonDocument
                                {
                                    { "LastEventOccurredAtUtc", -1 },
                                    { "LastEventVersion", -1 }
                                }
                            }
                        }),
                        MAX_TOMBSTONES
                    })
                }
            })
        };

        return Builders<ProductDocument>.Update.Pipeline(pipeline);
    }

    /// <summary>
    /// Computed: Gets the default variant for this product.
    /// Falls back to first variant if no default is set.
    /// </summary>
    [BsonIgnore]
    public ProductVariantDocument? DefaultVariant => 
        Variants.FirstOrDefault(v => v.IsDefault) ?? Variants.FirstOrDefault();

    /// <summary>
    /// Whether this product requires variant selection for cart/checkout.
    /// PHASE 11.6: Projected from PostgreSQL Product.HasVariants.
    /// This is the SOURCE OF TRUTH for cart variant enforcement.
    /// - true: Frontend MUST select a variant before adding to cart
    /// - false: Simple product, uses default variant automatically
    /// </summary>
    public bool HasVariants { get; set; }
    
    /// <summary>
    /// Computed: Helper to check if product actually has multiple variants in MongoDB.
    /// Different from HasVariants flag - this is structural, not business rule.
    /// </summary>
    [BsonIgnore]
    public bool HasMultipleVariants => Variants.Count > 1;
    
    #region Affiliate Marketing (AF-4C)

    /// <summary>
    /// Whether this product has affiliate marketing enabled.
    /// Updated via AffiliateProductPolicyChangedEvent.
    ///
    /// SECURITY: CommissionRate and MaxCommissionPerItem are NEVER exposed here.
    /// Only IsAffiliateEnabled is safe for public-facing projections.
    /// </summary>
    public bool IsAffiliateEnabled { get; set; }

    /// <summary>
    /// When affiliate was enabled for this product (for filtering/sorting).
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? AffiliateEnabledAtUtc { get; set; }

    #endregion

    #region Promotions Engine - Badge Display Fields
    
    /// <summary>
    /// Whether this product has an active promotion (offer) applied.
    /// Updated by PromotionProjectionConsumer when offers are created/updated/deleted.
    /// </summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Badge text to display on product card (e.g., "20% OFF", "Save $10")
    /// </summary>
    public string? PromotionBadge { get; set; }
    
    /// <summary>
    /// ID of the active promotion for deduplication/validation
    /// </summary>
    [BsonElement("ActivePromotionId")]
    [BsonSerializer(typeof(NullableGuidStringSerializer))]
    public Guid? ActivePromotionId { get; set; }
    
    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string? PromotionDiscountType { get; set; }
    
    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PromotionDiscountValue { get; set; }

    /// <summary>
    /// Optional cap used by the single storefront promotion composition engine.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PromotionMaxDiscountAmount { get; set; }
    
    // =========================================================================
    // SMART ENHANCEMENT: Time Decay & Merchandising Fields
    // Phase: Shopify-Grade Promotion-Aware Search Enhancements
    // =========================================================================
    
    /// <summary>
    /// When the active promotion started.
    /// Used for time decay in Elasticsearch ranking.
    /// Newer promotions rank higher than older ones.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PromotionStartDate { get; set; }
    
    /// <summary>
    /// When the active promotion ends.
    /// Used for countdown timers in frontend display.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PromotionEndDate { get; set; }
    
    /// <summary>
    /// Manual boost override from Promotion.SearchBoostOverride.
    /// When set (non-zero), adds to calculated boost for merchandising control.
    /// Synced from PostgreSQL Promotion via events.
    /// </summary>
    public double SearchBoostOverride { get; set; }
    
    /// <summary>
    /// Computed discounted price based on active promotion.
    /// Returns null if no promotion or if promotion is not applicable.
    /// </summary>
    [BsonIgnore]
    public decimal? DiscountedPrice
    {
        get
        {
            if (!HasActivePromotion)
                return null;

            return StorefrontPromotionPriceComposer.TryCompose(
                StorefrontVisiblePrice,
                PromotionDiscountType,
                PromotionDiscountValue,
                PromotionMaxDiscountAmount);
        }
    }
    
    #endregion

    #region Shipping Fields
    
    /// <summary>
    /// Product weight in kg for shipping calculations.
    /// Synced from PostgreSQL Product.Weight.
    /// Used for weight-based shipping quotes.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Weight { get; set; }
    
    #endregion
}

public readonly record struct AffiliatePublicPricingSnapshot(
    decimal? Price,
    decimal? MinVariantPrice,
    decimal? MaxVariantPrice,
    bool InStock,
    int TotalAvailableStock);
