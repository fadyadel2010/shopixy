namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Store config response for storefront (read-only).
/// GET /api/storefront/store-config
/// </summary>
public class StorefrontStoreConfigDto
{
    /// <summary>
    /// Tenant that owns this storefront configuration.
    /// Presentation clients use this as backend-issued scope evidence only.
    /// </summary>
    public Guid TenantId { get; set; }

    // Populated ONLY by the authenticated preview resolver; absent from public MapLive.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StorefrontPreviewChromeDto? PreviewChrome { get; set; }

    public string StoreName { get; set; } = string.Empty;
    public string? StoreDescription { get; set; }
    public string StorefrontTheme { get; set; } = "modern";
    public string ThemeId { get; set; } = string.Empty;
    public StorefrontThemeConfigDto Theme { get; set; } = new();
    public StorefrontBehaviorConfigDto Behavior { get; set; } = new();
    
    /// <summary>
    /// Storefront operational status: "live", "maintenance", "disabled".
    /// </summary>
    public string StorefrontStatus { get; set; } = "live";
    
    /// <summary>
    /// Optional message to display on maintenance page.
    /// </summary>
    public string? MaintenanceMessage { get; set; }
    
    public DateTime UpdatedAtUtc { get; set; }
    
    /// <summary>
    /// Dynamic page configurations (home, checkout, etc.).
    /// </summary>
    public StorefrontPagesDto Pages { get; set; } = new();

    /// <summary>
    /// Product-specific section templates keyed by template identifier.
    /// </summary>
    public Dictionary<string, StorefrontProductTemplateDto> ProductTemplates { get; set; } = new();
    
    /// <summary>
    /// Currency code (ISO 4217) for price display and cart (e.g., "EGP", "USD", "SAR").
    /// Configured in POS Admin Settings → Tax & Currency.
    /// </summary>
    public string Currency { get; set; } = "EGP";
    
    /// <summary>
    /// Default country code for the storefront (ISO 3166-1 alpha-2, e.g., "US", "EG", "SA").
    /// Used for shipping, address defaults, and regional settings.
    /// </summary>
    public string DefaultCountryCode { get; set; } = string.Empty;

    // =========================================================================
    // COMMERCE AI FEATURE (Phase 6)
    // =========================================================================

    /// <summary>
    /// Whether the Commerce AI assistant is enabled on the storefront.
    /// When false, the chat widget should not be rendered.
    /// Phase 6: Commerce AI Admin Control
    /// </summary>
    public bool CommerceAiEnabled { get; set; } = false;
    
    // Phase 7: Commerce AI Experience Config
    public string CommerceAiWidgetStyle { get; set; } = "minimal";
    public string CommerceAiWidgetPosition { get; set; } = "bottom-right";
    public bool CommerceAiAutoGreetingEnabled { get; set; } = false;
    public string CommerceAiAutoGreetingMessage { get; set; } = string.Empty;
    public string CommerceAiPersonality { get; set; } = "friendly";
}

public sealed class StorefrontPreviewChromeDto
{
    public string RuntimeUrl { get; set; } = string.Empty;
    public NavigationDto Navigation { get; set; } = new();
    public Dictionary<string, StorefrontFooterDto> FooterByLanguage { get; set; } = new();
}

public class StorefrontThemeConfigDto
{
    public StorefrontColorsConfigDto Colors { get; set; } = new();
    public StorefrontFontConfigDto Font { get; set; } = new();
    public StorefrontUiConfigDto Ui { get; set; } = new();
    public StorefrontLayoutConfigDto Layout { get; set; } = new();
}

public class StorefrontColorsConfigDto
{
    public string Primary { get; set; } = "#2563eb";
    public string Secondary { get; set; } = "#0f172a";
    public string Background { get; set; } = "#ffffff";
    public string Text { get; set; } = "#020617";
    public string Muted { get; set; } = "#64748b";
}

public class StorefrontFontConfigDto
{
    public string Family { get; set; } = "Inter";
    public string BaseSize { get; set; } = "16px";
}

public class StorefrontUiConfigDto
{
    public string BorderRadius { get; set; } = "8px";
    public string ButtonStyle { get; set; } = "solid";
}

public class StorefrontLayoutConfigDto
{
    public string ProductCardVariant { get; set; } = "default";
    public string HeaderVariant { get; set; } = "standard";
}

public class StorefrontBehaviorConfigDto
{
    public bool EnableGuestCheckout { get; set; } = true;
    public bool ShowOutOfStockProducts { get; set; } = false;
    public bool EnableSearch { get; set; } = true;
    public bool FailCheckoutOnPromotionError { get; set; } = true;
}

public class StorefrontPagesDto
{
    public StorefrontPageConfigDto Home { get; set; } = new();
    public StorefrontPageConfigDto Product { get; set; } = new();
    public StorefrontPageConfigDto Category { get; set; } = new();
}

public class StorefrontPageConfigDto
{
    public List<StorefrontSectionDto> Sections { get; set; } = new();
}

public class StorefrontProductTemplateDto
{
    public string TemplateId { get; set; } = "default";
    public string Name { get; set; } = "Default Template";
    public List<StorefrontSectionDto> Sections { get; set; } = new();
}

public class StorefrontSectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Order { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
}
