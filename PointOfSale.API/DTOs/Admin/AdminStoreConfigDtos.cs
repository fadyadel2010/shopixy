using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PointOfSale.Core.Constants;

namespace PointOfSale.API.DTOs.Admin;

#region Request/Response DTOs

/// <summary>
/// Request DTO for creating/updating store config.
/// PUT /api/admin/store-config
/// </summary>
public class AdminStoreConfigRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string StoreName { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? StoreDescription { get; set; }

    [StringLength(50)]
    public string? StorefrontTheme { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string ThemeId { get; set; } = "default";

    [Required]
    public AdminThemeConfigDto Theme { get; set; } = new();

    [Required]
    public AdminBehaviorConfigDto Behavior { get; set; } = new();

    /// <summary>
    /// Phase 7: Commerce AI Experience Config
    /// </summary>
    public AdminCommerceAiConfigDto CommerceAi { get; set; } = new();

    /// <summary>
    /// Storefront operational status: "live", "maintenance", "disabled".
    /// Controls whether customers can access the online store.
    /// </summary>
    [StringLength(20)]
    public string StorefrontStatus { get; set; } = "live";
    
    /// <summary>
    /// Optional message to display on maintenance page.
    /// </summary>
    [StringLength(500)]
    public string? MaintenanceMessage { get; set; }

    /// <summary>
    /// Required optimistic concurrency token for admin mutations.
    /// Accepts the current RowVersion returned by GET or the If-Match ETag value.
    /// </summary>
    public string? RowVersion { get; set; }

    /// <summary>
    /// Dynamic Page Layouts configuration (Sections).
    /// </summary>
    public Dictionary<string, object>? Pages { get; set; }

    /// <summary>
    /// Whitelist of allowed storefront status values.
    /// </summary>
    private static readonly HashSet<string> AllowedStorefrontStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "live", "maintenance", "disabled"
    };

    /// <summary>
    /// Validates the request and returns error message if invalid.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(StoreName))
            return "StoreName is required";

        if (string.IsNullOrWhiteSpace(ThemeId))
            return "ThemeId is required";

        if (!string.IsNullOrWhiteSpace(StorefrontTheme) &&
            !StorefrontThemes.IsSupported(StorefrontTheme))
            return "StorefrontTheme is not supported";

        if (!string.IsNullOrWhiteSpace(StorefrontStatus) && !AllowedStorefrontStatuses.Contains(StorefrontStatus))
            return $"StorefrontStatus must be one of: {string.Join(", ", AllowedStorefrontStatuses)}";

        var themeError = Theme?.Validate();
        if (themeError != null)
            return themeError;

        return CommerceAi?.Validate() ?? (CommerceAi == null ? "CommerceAi is required" : null);
    }
}

/// <summary>
/// Response DTO for store config operations.
/// </summary>
public class AdminStoreConfigResponse
{
    public Storefront.StorefrontPagesDto? LivePages { get; set; }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string RowVersion { get; set; } = "0";
    public string StoreName { get; set; } = string.Empty;
    public string? StoreDescription { get; set; }
    public string StorefrontTheme { get; set; } = "modern";
    public string ThemeId { get; set; } = string.Empty;
    public AdminThemeConfigDto Theme { get; set; } = new();
    public AdminBehaviorConfigDto Behavior { get; set; } = new();
    public AdminCommerceAiConfigDto CommerceAi { get; set; } = new();
    
    /// <summary>
    /// Storefront operational status: "live", "maintenance", "disabled".
    /// </summary>
    public string StorefrontStatus { get; set; } = "live";
    
    /// <summary>
    /// Optional message to display on maintenance page.
    /// </summary>
    public string? MaintenanceMessage { get; set; }
    
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    public Dictionary<string, object>? Pages { get; set; }
    public Dictionary<string, object>? ProductTemplates { get; set; }

    // Phase 9.3 Properties
    public string ThemePresetKey { get; set; } = "default";
    public string ThemeLivePresetKey { get; set; } = "default";
    public string ThemeDraftOverridesJson { get; set; } = "{}";
    public string ThemeLiveOverridesJson { get; set; } = "{}";
    public AdminThemeViewDto DraftTheme { get; set; } = new();
    public AdminThemeViewDto LiveTheme { get; set; } = new();
}

/// <summary>
/// Request DTO for updating the storefront operational status only.
/// PUT /api/admin/store-config/status
/// </summary>
public class AdminStorefrontStatusRequest
{
    /// <summary>
    /// Storefront operational status: "live", "maintenance", "disabled".
    /// Controls whether customers can access the online store.
    /// </summary>
    [StringLength(20)]
    public string StorefrontStatus { get; set; } = "live";

    /// <summary>
    /// Optional message to display on maintenance page.
    /// </summary>
    [StringLength(500)]
    public string? MaintenanceMessage { get; set; }

    /// <summary>
    /// Required optimistic concurrency token for admin mutations.
    /// Accepts the current RowVersion returned by GET or the If-Match ETag value.
    /// </summary>
    public string? RowVersion { get; set; }

    private static readonly HashSet<string> AllowedStorefrontStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "live", "maintenance", "disabled"
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(StorefrontStatus))
            return "StorefrontStatus is required";

        if (!AllowedStorefrontStatuses.Contains(StorefrontStatus))
            return $"StorefrontStatus must be one of: {string.Join(", ", AllowedStorefrontStatuses)}";

        return null;
    }
}

/// <summary>
/// Request DTO for activating a storefront theme shell.
/// POST /api/admin/store-config/theme/activate
/// </summary>
public class AdminThemeActivationRequest
{
    /// <summary>
    /// Storefront runtime theme identifier, for example "modern", "minimal", or "fashion".
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string ThemeId { get; set; } = string.Empty;

    /// <summary>
    /// Required optimistic concurrency token for admin mutations.
    /// Accepts the current RowVersion returned by GET or the If-Match ETag value.
    /// </summary>
    public string? RowVersion { get; set; }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ThemeId))
            return "ThemeId is required";

        if (!StorefrontThemes.IsSupported(ThemeId))
            return "ThemeId is not supported";

        return null;
    }
}

public class AdminThemeViewDto
{
    public string PresetKey { get; set; } = "default";
    public AdminThemeViewColorsDto Colors { get; set; } = new();
    public string Font { get; set; } = "Inter";
    public string BaseSize { get; set; } = "16px";
    public int BorderRadius { get; set; } = 8;
}

public class AdminThemeViewColorsDto
{
    public string Primary { get; set; } = "#2563eb";
    public string Secondary { get; set; } = "#0f172a";
    public string Background { get; set; } = "#ffffff";
    public string Text { get; set; } = "#020617";
    public string Muted { get; set; } = "#64748b";
}

public class ThemePresetDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsRtl { get; set; }
    public string PreviewImageUrl { get; set; } = string.Empty;
    public Dictionary<string, object>? BaseTheme { get; set; }
}

#endregion

#region Theme Config DTOs

public class AdminThemeConfigDto
{
    public AdminColorsConfigDto Colors { get; set; } = new();
    public AdminFontConfigDto Font { get; set; } = new();
    public AdminUiConfigDto Ui { get; set; } = new();
    public AdminLayoutConfigDto Layout { get; set; } = new();

    public string? Validate()
    {
        var colorsError = Colors?.Validate();
        if (colorsError != null)
            return colorsError;

        var fontError = Font?.Validate();
        if (fontError != null)
            return fontError;

        var uiError = Ui?.Validate();
        if (uiError != null)
            return uiError;

        var layoutError = Layout?.Validate();
        if (layoutError != null)
            return layoutError;

        return null;
    }
}

public class AdminColorsConfigDto
{
    private static readonly Regex HexColorRegex = new(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    public string Primary { get; set; } = "#2563eb";
    public string Secondary { get; set; } = "#0f172a";
    public string Background { get; set; } = "#ffffff";
    public string Text { get; set; } = "#020617";
    public string Muted { get; set; } = "#64748b";

    public string? Validate()
    {
        if (!IsValidHexColor(Primary))
            return "Primary color must be a valid hex color (e.g., #2563eb)";
        if (!IsValidHexColor(Secondary))
            return "Secondary color must be a valid hex color";
        if (!IsValidHexColor(Background))
            return "Background color must be a valid hex color";
        if (!IsValidHexColor(Text))
            return "Text color must be a valid hex color";
        if (!IsValidHexColor(Muted))
            return "Muted color must be a valid hex color";

        return null;
    }

    private static bool IsValidHexColor(string? color)
    {
        return !string.IsNullOrWhiteSpace(color) && HexColorRegex.IsMatch(color);
    }
}

public class AdminFontConfigDto
{
    /// <summary>
    /// Whitelisted font families (safe for CSS injection prevention).
    /// </summary>
    public static readonly HashSet<string> AllowedFontFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inter", "Roboto", "Open Sans", "Lato", "Poppins", 
        "Montserrat", "Outfit", "Cairo", "Tajawal", "Noto Sans Arabic"
    };

    public string Family { get; set; } = "Inter";
    public string BaseSize { get; set; } = "16px";

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Family))
            return "Font family is required";

        if (!AllowedFontFamilies.Contains(Family))
            return $"Font family '{Family}' is not allowed. Allowed: {string.Join(", ", AllowedFontFamilies)}";

        if (string.IsNullOrWhiteSpace(BaseSize))
            return "Base size is required";

        return null;
    }
}

public class AdminUiConfigDto
{
    private static readonly HashSet<string> AllowedButtonStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "solid", "outline"
    };

    public string BorderRadius { get; set; } = "8px";
    public string ButtonStyle { get; set; } = "solid";

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(BorderRadius))
            return "Border radius is required";

        if (!AllowedButtonStyles.Contains(ButtonStyle))
            return $"Button style must be one of: {string.Join(", ", AllowedButtonStyles)}";

        return null;
    }
}

public class AdminLayoutConfigDto
{
    private static readonly HashSet<string> AllowedProductCardVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "compact", "detailed"
    };

    private static readonly HashSet<string> AllowedHeaderVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard", "centered", "minimal"
    };

    public string ProductCardVariant { get; set; } = "default";
    public string HeaderVariant { get; set; } = "standard";

    public string? Validate()
    {
        if (!AllowedProductCardVariants.Contains(ProductCardVariant))
            return $"Product card variant must be one of: {string.Join(", ", AllowedProductCardVariants)}";

        if (!AllowedHeaderVariants.Contains(HeaderVariant))
            return $"Header variant must be one of: {string.Join(", ", AllowedHeaderVariants)}";

        return null;
    }
}

public class AdminBehaviorConfigDto
{
    public bool EnableGuestCheckout { get; set; } = true;
    public bool ShowOutOfStockProducts { get; set; } = false;
    public bool EnableSearch { get; set; } = true;
    public bool FailCheckoutOnPromotionError { get; set; } = true;
}

public class AdminCommerceAiConfigDto
{
    public bool Enabled { get; set; }
    public string WidgetStyle { get; set; } = "minimal";
    public string WidgetPosition { get; set; } = "bottom-right";
    public bool AutoGreetingEnabled { get; set; }
    public string AutoGreetingMessage { get; set; } = string.Empty;
    public string Personality { get; set; } = "friendly";

    public string? Validate()
    {
        if (WidgetStyle is not ("minimal" or "floating" or "sales" or "compact")) return "Unsupported widget style";
        if (WidgetPosition is not ("bottom-left" or "bottom-right" or "middle-right")) return "Unsupported widget position";
        if (Personality is not ("friendly" or "professional" or "sales-focused")) return "Unsupported assistant personality";
        if ((AutoGreetingMessage?.Length ?? 0) > 120) return "Greeting cannot exceed 120 characters";
        return null;
    }
}

// The narrow experience endpoint accepts an empty optional greeting even when
// disabled. Preserve the legacy full-configuration normalization contract.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public class AdminCommerceAiExperienceRequest
{
    public bool Enabled { get; set; }
    public string WidgetStyle { get; set; } = "minimal";
    public string WidgetPosition { get; set; } = "bottom-right";
    public bool AutoGreetingEnabled { get; set; }
    public string? AutoGreetingMessage { get; set; }
    public string Personality { get; set; } = "friendly";
    public AdminCommerceAiConfigDto ToConfig() => new() { Enabled = Enabled, WidgetStyle = WidgetStyle, WidgetPosition = WidgetPosition, AutoGreetingEnabled = AutoGreetingEnabled, AutoGreetingMessage = AutoGreetingMessage ?? "", Personality = Personality };
}

#endregion

#region API Error Response

/// <summary>
/// Standard error response for Store Config Admin APIs.
/// </summary>
public class AdminStoreConfigApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;

    public AdminStoreConfigApiErrorResponse() { }

    public AdminStoreConfigApiErrorResponse(string code, string message, string traceId)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    public static AdminStoreConfigApiErrorResponse TenantHeaderMissing(string traceId) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", traceId);

    public static AdminStoreConfigApiErrorResponse TenantHeaderInvalid(string traceId) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id header must be a valid GUID", traceId);

    public static AdminStoreConfigApiErrorResponse TenantForbidden(string traceId) =>
        new("TENANT_FORBIDDEN", "You do not have access to this tenant.", traceId);

    public static AdminStoreConfigApiErrorResponse InvalidConfig(string message, string traceId) =>
        new("INVALID_STORE_CONFIG", message, traceId);

    public static AdminStoreConfigApiErrorResponse PreconditionRequired(string traceId) =>
        new("PRECONDITION_REQUIRED", "If-Match or RowVersion is required for this mutation.", traceId);

    public static AdminStoreConfigApiErrorResponse ConcurrencyConflict(string traceId) =>
        new("CONCURRENCY_CONFLICT", "This configuration was modified by another admin. Please refresh and try again.", traceId);

    public static AdminStoreConfigApiErrorResponse NotFound(string traceId) =>
        new("STORE_CONFIG_NOT_FOUND", "Store configuration not found for this tenant", traceId);

    public static AdminStoreConfigApiErrorResponse PublishTargetNotFound(string message, string traceId) =>
        new("PUBLISH_TARGET_NOT_FOUND", message, traceId);

    public static AdminStoreConfigApiErrorResponse InternalError(string traceId) =>
        new("INTERNAL_ERROR", "An internal error occurred. Please try again later.", traceId);
}

#endregion
