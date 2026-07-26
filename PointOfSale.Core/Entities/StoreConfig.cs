using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Store configuration entity - single source of truth for store appearance and behavior.
/// Phase 7.1: Theme & Store Config System
/// 
/// ARCHITECTURAL RULES:
/// - One StoreConfig per Tenant (unique TenantId)
/// - Managed ONLY via POS Admin APIs
/// - Storefront is read-only
/// - Config-driven, not conditional UI logic
/// </summary>
public class StoreConfig : ITenantOwned
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant that owns this store config.
    /// CRITICAL: Must be unique per tenant.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Store name displayed on the storefront.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;
    
    /// <summary>
    /// Store description for SEO and about sections.
    /// </summary>
    public string? StoreDescription { get; set; }

    /// <summary>
    /// Theme identifier. Default theme is "default".
    /// Future: Will support multiple registered themes.
    /// </summary>
    public string ThemeId { get; set; } = "default";

    /// <summary>
    /// Theme appearance configuration (colors, fonts, UI, layout).
    /// Stored as JSON in PostgreSQL.
    /// </summary>
    public StoreThemeConfig Theme { get; set; } = new();

    /// <summary>
    /// Behavior flags controlling storefront features.
    /// Stored as JSON in PostgreSQL.
    /// </summary>
    public StoreBehaviorConfig Behavior { get; set; } = new();

    /// <summary>
    /// Storefront operational status.
    /// Controls whether customers can access the online store.
    /// Values: "live", "maintenance", "disabled"
    /// </summary>
    public string StorefrontStatus { get; set; } = "live";
    
    /// <summary>
    /// Optional message to display on maintenance page.
    /// Only shown when StorefrontStatus is "maintenance".
    /// </summary>
    public string? MaintenanceMessage { get; set; }
    
    /// <summary>
    /// When the config was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the config was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON blob storing the dynamic configuration for pages (sections) and advanced theme overrides.
    /// This allows for structural changes to the storefront without schema migrations.
    /// Contains: { "pages": { ... }, "theme": { ... } }
    /// </summary>
    public string CustomizationJson { get; set; } = "{}";

    /// <summary>
    /// Selected Theme Preset Key (e.g., "modern-light").
    /// </summary>
    public string ThemePresetKey { get; set; } = "default";

    /// <summary>
    /// Published preset identity used by public storefront rendering.
    /// Draft preset selection must not change this value until theme publish.
    /// </summary>
    public string ThemeLivePresetKey { get; set; } = "default";

    /// <summary>
    /// Draft Theme Overrides (Admin edits) - JSON.
    /// </summary>
    public string ThemeDraftOverridesJson { get; set; } = "{}";

    /// <summary>
    /// Published Theme Overrides (Live Storefront) - JSON.
    /// </summary>
    public string ThemeLiveOverridesJson { get; set; } = "{}";

    /// <summary>
    /// Version history of the published configurations.
    /// JSON Array of VersionSnapshot objects.
    /// </summary>
    public string VersionsJson { get; set; } = "[]";

    /// <summary>
    /// The currently live version number. 
    /// 0 implies initial state or no versioning yet.
    /// </summary>
    public int LiveVersion { get; set; } = 0;

    /// <summary>
    /// Optimistic concurrency token (Row Version).
    /// CRITICAL: Prevents silent overwrites when multiple admins edit simultaneously.
    /// PostgreSQL: Mapped as "xmin" system column or explicit bytea column.
    /// Phase: Storefront Admin Hardening - P0
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Promotion-aware search ranking configuration.
    /// Controls how promotions affect product search ranking in Elasticsearch.
    /// Phase: Shopify-Grade Promotion-Aware Search
    /// </summary>
    public PromotionRankingConfig PromotionRanking { get; set; } = new();

    // =========================================================================
    // COMMERCE AI CONFIGURATION (Phase 6)
    // Admin governance layer for AI assistant on storefront
    // =========================================================================

    /// <summary>
    /// Whether the Commerce AI assistant is enabled on the storefront.
    /// When disabled, the chat widget does not appear.
    /// CRITICAL: Default is FALSE - must be explicitly enabled by admin.
    /// Phase 6: Commerce AI Admin Control
    /// </summary>
    public bool CommerceAiEnabled { get; set; } = false;

    /// <summary>
    /// When the Commerce AI was enabled by admin.
    /// Null if never enabled or currently disabled.
    /// </summary>
    public DateTime? CommerceAiEnabledAt { get; set; }

    /// <summary>
    /// Admin who last toggled the Commerce AI feature.
    /// For audit trail purposes.
    /// </summary>
    public string? CommerceAiUpdatedBy { get; set; }

    /// <summary>
    /// Widget visual style.
    /// Values: "minimal", "floating", "sales", "compact"
    /// Default: "minimal"
    /// </summary>
    public string CommerceAiWidgetStyle { get; set; } = "minimal";

    /// <summary>
    /// Widget position on screen.
    /// Values: "bottom-right", "bottom-left", "middle-right"
    /// Default: "bottom-right"
    /// </summary>
    public string CommerceAiWidgetPosition { get; set; } = "bottom-right";

    /// <summary>
    /// Whether to send an automatic greeting message when the widget loads.
    /// </summary>
    public bool CommerceAiAutoGreetingEnabled { get; set; } = false;

    /// <summary>
    /// The automatic greeting message content.
    /// Max 120 chars.
    /// </summary>
    public string CommerceAiAutoGreetingMessage { get; set; } = string.Empty;

    /// <summary>
    /// AI Assistant personality/tone.
    /// Values: "friendly", "professional", "sales-focused"
    /// Default: "friendly"
    /// </summary>
    public string CommerceAiPersonality { get; set; } = "friendly";
}

/// <summary>
/// Theme configuration containing all visual customization options.
/// </summary>
public class StoreThemeConfig
{
    public ColorsConfig Colors { get; set; } = new();
    public FontConfig Font { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
}

/// <summary>
/// Color configuration for the storefront theme.
/// All colors must be valid hex format (#RRGGBB or #RGB).
/// </summary>
public class ColorsConfig
{
    /// <summary>Primary color for buttons, links, accents.</summary>
    public string Primary { get; set; } = "#2563eb";
    
    /// <summary>Secondary color for backgrounds, cards.</summary>
    public string Secondary { get; set; } = "#0f172a";
    
    /// <summary>Main background color.</summary>
    public string Background { get; set; } = "#ffffff";
    
    /// <summary>Primary text color.</summary>
    public string Text { get; set; } = "#020617";
    
    /// <summary>Muted/secondary text color.</summary>
    public string Muted { get; set; } = "#64748b";
}

/// <summary>
/// Font configuration for the storefront.
/// Font family must be from whitelisted fonts.
/// </summary>
public class FontConfig
{
    /// <summary>
    /// Font family name. Must be from whitelist:
    /// Inter, Roboto, Open Sans, Lato, Poppins, Montserrat, Outfit, Cairo, Tajawal
    /// </summary>
    public string Family { get; set; } = "Inter";
    
    /// <summary>Base font size with unit (e.g., "16px", "1rem").</summary>
    public string BaseSize { get; set; } = "16px";
}

/// <summary>
/// UI component configuration.
/// </summary>
public class UiConfig
{
    /// <summary>Border radius for components (e.g., "8px", "0.5rem").</summary>
    public string BorderRadius { get; set; } = "8px";
    
    /// <summary>Button style variant: "solid" or "outline".</summary>
    public string ButtonStyle { get; set; } = "solid";
}

/// <summary>
/// Layout configuration for component variants.
/// Variants are string identifiers, not conditional logic.
/// </summary>
public class LayoutConfig
{
    /// <summary>Product card display variant: "default", "compact", "detailed".</summary>
    public string ProductCardVariant { get; set; } = "default";
    
    /// <summary>Header layout variant: "standard", "centered", "minimal".</summary>
    public string HeaderVariant { get; set; } = "standard";
}

/// <summary>
/// Behavior configuration controlling storefront features.
/// </summary>
public class StoreBehaviorConfig
{
    /// <summary>Allow guest checkout without registration.</summary>
    public bool EnableGuestCheckout { get; set; } = true;
    
    /// <summary>Show out-of-stock products (as unavailable).</summary>
    public bool ShowOutOfStockProducts { get; set; } = false;
    
    /// <summary>Enable product search functionality.</summary>
    public bool EnableSearch { get; set; } = true;

    /// <summary>
    /// When true, checkout fails closed if promotion calculation errors occur.
    /// SECURITY DEFAULT: always fail closed on promotion errors.
    /// </summary>
    public bool FailCheckoutOnPromotionError { get; set; } = true;
}

/// <summary>
/// Promotion-aware search ranking configuration.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Controls how promotions affect product search ranking in Elasticsearch.
/// Uses function_score with additive boost_mode to preserve text relevance.
/// 
/// Design Principles:
/// - Promotions BOOST, never BURY: text relevance is preserved
/// - Bigger discounts rank higher than smaller discounts
/// - Time decay: newer promotions rank higher than older ones
/// - Category-specific: different categories can have different strategies
/// - Tenant-configurable via StoreConfig
/// - Cached in Redis for performance
/// - Defaults are conservative (low boost values)
/// 
/// NOTE: This is a value object owned by StoreConfig.
/// It is serialized as JSON inside StoreConfig via OwnsOne.
/// </summary>
public class PromotionRankingConfig
{
    /// <summary>
    /// Whether promotion-aware ranking is enabled.
    /// When disabled, search ranking is purely text-relevance based.
    /// Default: true (promotions boost ranking)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base boost score added to products with any active promotion.
    /// This is the minimum boost any promoted product receives.
    /// Additive to text relevance score (not multiplicative).
    /// Default: 5.0
    /// Recommended range: 1-10
    /// </summary>
    public double BaseBoost { get; set; } = 5.0;

    /// <summary>
    /// Additional boost for products with high-value discounts.
    /// Added on top of BaseBoost when discount value >= HighDiscountThreshold.
    /// Default: 10.0
    /// Recommended range: 5-20
    /// </summary>
    public double HighDiscountBoost { get; set; } = 10.0;

    /// <summary>
    /// Threshold for high-discount boost eligibility.
    /// Products with PromotionDiscountValue >= this value get HighDiscountBoost.
    /// For percentage discounts: represents % (e.g., 20 = 20% off)
    /// For fixed discounts: represents absolute value
    /// Default: 20 (20% or $20 off)
    /// </summary>
    public double HighDiscountThreshold { get; set; } = 20.0;

    /// <summary>
    /// Maximum total boost a product can receive from promotions.
    /// Prevents runaway scoring from stacking multiple boosts.
    /// Set to 0 for unlimited boost.
    /// Default: 25.0 (BaseBoost + HighDiscountBoost cap)
    /// </summary>
    public double MaxTotalBoost { get; set; } = 25.0;

    // =========================================================================
    // SMART ENHANCEMENT: Time Decay Configuration
    // Newer promotions rank higher than older ones
    // =========================================================================

    /// <summary>
    /// Whether time-based decay is enabled for promotion boosts.
    /// When enabled, newer promotions rank higher than older ones.
    /// Uses Gaussian decay function on promotion start date.
    /// Default: true
    /// </summary>
    public bool EnableTimeDecay { get; set; } = true;

    /// <summary>
    /// Scale parameter for time decay (days).
    /// Promotions older than this lose significant boost.
    /// Uses Gaussian decay: boost = base * exp(-0.5 * (age/scale)^2)
    /// Default: 7.0 (1 week - 50% decay at 7 days)
    /// Recommended range: 3-30 days
    /// </summary>
    public double TimeDecayScaleDays { get; set; } = 7.0;

    /// <summary>
    /// Offset parameter for time decay (days).
    /// Promotions younger than this get full boost (no decay).
    /// Default: 1.0 (1 day grace period)
    /// </summary>
    public double TimeDecayOffsetDays { get; set; } = 1.0;

    /// <summary>
    /// Minimum decay factor (0 to 1).
    /// Prevents old promotions from losing ALL their boost.
    /// A value of 0.2 means old promotions keep at least 20% of boost.
    /// Default: 0.3 (old promotions keep 30% boost minimum)
    /// </summary>
    public double TimeDecayMinimum { get; set; } = 0.3;

    // =========================================================================
    // SMART ENHANCEMENT: Category-Specific Boost Overrides
    // Different categories need different merchandising strategies
    // =========================================================================

    /// <summary>
    /// Per-category promotion boost overrides.
    /// When empty, all categories use global settings.
    /// Key: CategoryId (GUID as string)
    /// Value: CategoryPromotionBoostOverride
    /// NOTE: Marked NotMapped because EF Core cannot map Dictionary types.
    /// This field is serialized as part of the parent JSON column.
    /// </summary>
    [NotMapped]
    public Dictionary<string, CategoryPromotionBoostOverride> CategoryOverrides { get; set; } = new();
}

/// <summary>
/// Category-specific promotion boost configuration.
/// Allows different merchandising strategies per category.
/// 
/// Examples:
/// - Electronics: Stronger boost (competing market)
/// - Grocery: Lighter boost (commodity products)
/// - Fashion: Time-sensitive (seasonal)
/// 
/// NOTE: This is a value object owned by PromotionRankingConfig.
/// It is serialized as JSON as part of PromotionRankingConfig.
/// </summary>
public class CategoryPromotionBoostOverride
{
    /// <summary>
    /// Override for BaseBoost in this category.
    /// When null, uses global PromotionRankingConfig.BaseBoost.
    /// </summary>
    public double? BaseBoost { get; set; }

    /// <summary>
    /// Override for HighDiscountBoost in this category.
    /// When null, uses global PromotionRankingConfig.HighDiscountBoost.
    /// </summary>
    public double? HighDiscountBoost { get; set; }

    /// <summary>
    /// Override for HighDiscountThreshold in this category.
    /// When null, uses global PromotionRankingConfig.HighDiscountThreshold.
    /// </summary>
    public double? HighDiscountThreshold { get; set; }

    /// <summary>
    /// Override for TimeDecayScaleDays in this category.
    /// Fashion might use 3 days (trend-sensitive).
    /// Electronics might use 14 days (slower decay).
    /// When null, uses global PromotionRankingConfig.TimeDecayScaleDays.
    /// </summary>
    public double? TimeDecayScaleDays { get; set; }

    /// <summary>
    /// Multiplier applied on top of calculated boost for this category.
    /// 1.5 = 50% stronger boost in this category.
    /// 0.5 = 50% weaker boost in this category.
    /// Default: 1.0 (no change)
    /// </summary>
    public double BoostMultiplier { get; set; } = 1.0;
}

