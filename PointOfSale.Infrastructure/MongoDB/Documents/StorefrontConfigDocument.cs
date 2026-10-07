using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB Read Model for Storefront Configuration.
/// Maps to 'storefront_configs' collection.
/// Single document per tenant.
/// </summary>
public class StorefrontConfigDocument
{
    [BsonId]
    public string Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    public string TenantSlug { get; set; } = string.Empty;

    /// <summary>
    /// Schema version for the document structure itself.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Version of the theme edits (incremented on save).
    /// </summary>
    public int ThemeVersion { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Newest StoreConfig event applied to this read model.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }

    /// <summary>Authoritative SQL publication revision; independent of event schema versions.</summary>
    public int LiveVersion { get; set; }

    /// <summary>
    /// Newest tenant-currency authority event applied to the Currency field.
    /// Kept separate from StoreConfig lifecycle freshness so currency sync cannot
    /// suppress unrelated storefront configuration projections.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastCurrencyEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastCurrencyEventOccurredAtUtc { get; set; }

    public int LastCurrencyEventVersion { get; set; }

    public string StoreName { get; set; } = string.Empty;
    public string? StoreDescription { get; set; }
    public string StorefrontTheme { get; set; } = "modern";
    public string StorefrontStatus { get; set; } = "live";
    public string? MaintenanceMessage { get; set; }

    /// <summary>
    /// Currency code for the storefront (ISO 4217, e.g., "EGP", "USD", "SAR").
    /// Used by cart, checkout, and price display.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Visual theme configuration (colors, fonts, tokens).
    /// </summary>
    public StorefrontTheme Theme { get; set; } = new();

    /// <summary>
    /// Behavior flags (guest checkout, etc).
    /// </summary>
    public BehaviorConfig Behavior { get; set; } = new();

    /// <summary>
    /// Page configurations (layout, sections).
    /// </summary>
    public StorefrontPages Pages { get; set; } = new();

    /// <summary>
    /// Product page templates keyed by template identifier.
    /// </summary>
    public Dictionary<string, StorefrontProductTemplate> ProductTemplates { get; set; } = new();

    /// <summary>
    /// Independent popup marketing offers projected for fast storefront reads.
    /// </summary>
    public List<StorefrontSmartOfferDocument> SmartOffers { get; set; } = new();

    /// <summary>
    /// Promotion ranking configuration projected from StoreConfig for storefront search.
    /// </summary>
    public PromotionRankingConfig PromotionRanking { get; set; } = new();

    // =========================================================================
    // COMMERCE AI (Phase 6)
    // =========================================================================

    /// <summary>
    /// Whether the Commerce AI assistant is enabled on the storefront.
    /// If false, the chat widget should not be rendered.
    /// </summary>
    public bool CommerceAiEnabled { get; set; } = false;
    public string CommerceAiWidgetStyle { get; set; } = "minimal";
    public string CommerceAiWidgetPosition { get; set; } = "bottom-right";
    public bool CommerceAiAutoGreetingEnabled { get; set; } = false;
    public string CommerceAiAutoGreetingMessage { get; set; } = string.Empty;
    public string CommerceAiPersonality { get; set; } = "friendly";
}

public class BehaviorConfig
{
    public bool EnableGuestCheckout { get; set; } = true;
    public bool ShowOutOfStockProducts { get; set; } = false;
    public bool EnableSearch { get; set; } = true;
    public bool FailCheckoutOnPromotionError { get; set; } = true;
}

public class StorefrontTheme
{
    public string Id { get; set; } = "default";
    public string Mode { get; set; } = "light";
    public ThemeTokens Tokens { get; set; } = new();
}

public class ThemeTokens
{
    public Dictionary<string, string> Colors { get; set; } = new();
    public Dictionary<string, string> Typography { get; set; } = new();
    public Dictionary<string, string> Radius { get; set; } = new();
}

public class StorefrontPages
{
    public StorefrontPageConfig Home { get; set; } = new();
    
    /// <summary>
    /// Product Detail Page (PDP) configuration.
    /// </summary>
    public StorefrontPageConfig Product { get; set; } = new();

    /// <summary>
    /// Category Listing Page (PLP) configuration.
    /// </summary>
    public StorefrontPageConfig Category { get; set; } = new();
}

public class StorefrontPageConfig
{
    public List<StorefrontSection> Sections { get; set; } = new();
}

public class StorefrontProductTemplate
{
    public string TemplateId { get; set; } = "default";
    public string Name { get; set; } = "Default Template";
    public List<StorefrontSection> Sections { get; set; } = new();
}

public class StorefrontSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
    
    /// <summary>
    /// Section-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}

public class StorefrontSmartOfferDocument
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public SmartOfferStatus Status { get; set; } = SmartOfferStatus.Inactive;
    public int Priority { get; set; }
    public SmartOfferTriggerType TriggerType { get; set; }
    public SmartOfferPagesScope PagesScope { get; set; } = SmartOfferPagesScope.All;
    public int? DelaySeconds { get; set; }
    public int? ScrollPercent { get; set; }
    public bool ShowOncePerSession { get; set; }
    public int? MaxDisplaysPerUser { get; set; }
    public string PopupTitle { get; set; } = string.Empty;
    public string? PopupDescription { get; set; }
    public string ButtonText { get; set; } = string.Empty;
    public string? CouponCode { get; set; }
    public string? ImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
}
