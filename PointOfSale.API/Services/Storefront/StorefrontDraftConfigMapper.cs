using System.Text.Json;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Storefront;

internal static class StorefrontDraftConfigMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static StorefrontStoreConfigDto Map(
        StoreConfig config,
        ThemePreset? themePreset,
        string currency,
        string defaultCountryCode,
        Action<Exception, string>? errorCallback = null)
    {
        return Map(
            config,
            themePreset,
            currency,
            defaultCountryCode,
            useDraftContent: true,
            storefrontThemeOverride: null,
            errorCallback: errorCallback);
    }

    public static StorefrontStoreConfigDto MapLive(
        StoreConfig config,
        ThemePreset? themePreset,
        string currency,
        string defaultCountryCode,
        Action<Exception, string>? errorCallback = null,
        string? storefrontThemeOverride = null)
    {
        return Map(
            config,
            themePreset,
            currency,
            defaultCountryCode,
            useDraftContent: false,
            storefrontThemeOverride: storefrontThemeOverride,
            errorCallback: errorCallback);
    }

    private static StorefrontStoreConfigDto Map(
        StoreConfig config,
        ThemePreset? themePreset,
        string currency,
        string defaultCountryCode,
        bool useDraftContent,
        string? storefrontThemeOverride,
        Action<Exception, string>? errorCallback)
    {
        var storefrontTheme = !string.IsNullOrWhiteSpace(storefrontThemeOverride)
            ? StorefrontThemes.NormalizeOrDefault(storefrontThemeOverride)
            : ExtractStorefrontTheme(config.CustomizationJson, config.ThemeId, errorCallback);

        return new StorefrontStoreConfigDto
        {
            TenantId = config.TenantId,
            StoreName = config.StoreName,
            StoreDescription = config.StoreDescription,
            StorefrontTheme = storefrontTheme,
            ThemeId = themePreset?.Key ?? "default",
            Theme = MapThemeConfig(config, themePreset, useDraftOverrides: useDraftContent, storefrontTheme, errorCallback),
            Behavior = new StorefrontBehaviorConfigDto
            {
                EnableGuestCheckout = config.Behavior.EnableGuestCheckout,
                ShowOutOfStockProducts = config.Behavior.ShowOutOfStockProducts,
                EnableSearch = config.Behavior.EnableSearch,
                // Respect the configured value; defaults to true (fail-closed) for security
                FailCheckoutOnPromotionError = config.Behavior.FailCheckoutOnPromotionError
            },
            StorefrontStatus = config.StorefrontStatus,
            MaintenanceMessage = config.MaintenanceMessage,
            UpdatedAtUtc = config.UpdatedAtUtc,
            Pages = MapPages(config.CustomizationJson, useDraftContent, errorCallback),
            ProductTemplates = MapProductTemplates(config.CustomizationJson, useDraftContent, errorCallback),
            Currency = currency,
            DefaultCountryCode = defaultCountryCode,
            CommerceAiEnabled = config.CommerceAiEnabled,
            CommerceAiWidgetStyle = config.CommerceAiWidgetStyle,
            CommerceAiWidgetPosition = config.CommerceAiWidgetPosition,
            CommerceAiAutoGreetingEnabled = config.CommerceAiAutoGreetingEnabled,
            CommerceAiAutoGreetingMessage = config.CommerceAiAutoGreetingMessage,
            CommerceAiPersonality = config.CommerceAiPersonality
        };
    }

    public static string ExtractStorefrontTheme(string? customizationJson, string? fallbackThemeId = null, Action<Exception, string>? errorCallback = null)
    {
        if (!string.IsNullOrWhiteSpace(customizationJson))
        {
            try
            {
                using var document = JsonDocument.Parse(customizationJson);
                if (document.RootElement.TryGetProperty("storefrontTheme", out var storefrontTheme) &&
                    storefrontTheme.ValueKind == JsonValueKind.String)
                {
                    var value = storefrontTheme.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return StorefrontThemes.NormalizeOrDefault(value);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore malformed customization JSON and fall back below.
                errorCallback?.Invoke(ex, $"ExtractStorefrontTheme: {customizationJson}");
            }
        }

        return StorefrontThemes.NormalizeOrDefault(fallbackThemeId);
    }

    internal static StorefrontThemeConfigDto MapThemeConfig(
        StoreConfig config,
        ThemePreset? themePreset,
        bool useDraftOverrides,
        string? storefrontTheme = null,
        Action<Exception, string>? errorCallback = null)
    {
        var activatedTheme = !string.IsNullOrWhiteSpace(storefrontTheme)
            ? StorefrontThemes.NormalizeOrDefault(storefrontTheme)
            : ExtractStorefrontTheme(config.CustomizationJson, config.ThemeId, errorCallback);

        var theme = CreateThemeDto(new StoreThemeConfig());
        ApplyThemeConfig(theme, StorefrontThemes.GetThemeConfig(activatedTheme));

        if (themePreset != null &&
            !string.Equals(themePreset.Key, "default", StringComparison.OrdinalIgnoreCase))
        {
            ApplyThemePreset(theme, themePreset, errorCallback);
        }

        ApplyLegacyTenantThemeOverrides(theme, config.Theme);
        ApplyThemeOverrides(theme, useDraftOverrides ? config.ThemeDraftOverridesJson : config.ThemeLiveOverridesJson, errorCallback);

        return theme;
    }

    private static StorefrontThemeConfigDto CreateThemeDto(StoreThemeConfig theme)
    {
        return new StorefrontThemeConfigDto
        {
            Colors = new StorefrontColorsConfigDto
            {
                Primary = theme.Colors.Primary,
                Secondary = theme.Colors.Secondary,
                Background = theme.Colors.Background,
                Text = theme.Colors.Text,
                Muted = theme.Colors.Muted
            },
            Font = new StorefrontFontConfigDto
            {
                Family = theme.Font.Family,
                BaseSize = theme.Font.BaseSize
            },
            Ui = new StorefrontUiConfigDto
            {
                BorderRadius = theme.Ui.BorderRadius,
                ButtonStyle = theme.Ui.ButtonStyle
            },
            Layout = new StorefrontLayoutConfigDto
            {
                ProductCardVariant = theme.Layout.ProductCardVariant,
                HeaderVariant = theme.Layout.HeaderVariant
            }
        };
    }

    private static void ApplyThemeConfig(StorefrontThemeConfigDto theme, StoreThemeConfig source)
    {
        theme.Colors.Primary = source.Colors.Primary;
        theme.Colors.Secondary = source.Colors.Secondary;
        theme.Colors.Background = source.Colors.Background;
        theme.Colors.Text = source.Colors.Text;
        theme.Colors.Muted = source.Colors.Muted;
        theme.Font.Family = source.Font.Family;
        theme.Font.BaseSize = source.Font.BaseSize;
        theme.Ui.BorderRadius = source.Ui.BorderRadius;
        theme.Ui.ButtonStyle = source.Ui.ButtonStyle;
        theme.Layout.ProductCardVariant = source.Layout.ProductCardVariant;
        theme.Layout.HeaderVariant = source.Layout.HeaderVariant;
    }

    private static void ApplyLegacyTenantThemeOverrides(StorefrontThemeConfigDto theme, StoreThemeConfig? legacyTheme)
    {
        if (legacyTheme == null)
        {
            return;
        }

        var frameworkDefaults = new StoreThemeConfig();

        ApplyIfCustomized(legacyTheme.Colors.Primary, frameworkDefaults.Colors.Primary, value => theme.Colors.Primary = value);
        ApplyIfCustomized(legacyTheme.Colors.Secondary, frameworkDefaults.Colors.Secondary, value => theme.Colors.Secondary = value);
        ApplyIfCustomized(legacyTheme.Colors.Background, frameworkDefaults.Colors.Background, value => theme.Colors.Background = value);
        ApplyIfCustomized(legacyTheme.Colors.Text, frameworkDefaults.Colors.Text, value => theme.Colors.Text = value);
        ApplyIfCustomized(legacyTheme.Colors.Muted, frameworkDefaults.Colors.Muted, value => theme.Colors.Muted = value);
        ApplyIfCustomized(legacyTheme.Font.Family, frameworkDefaults.Font.Family, value => theme.Font.Family = value);
        ApplyIfCustomized(legacyTheme.Font.BaseSize, frameworkDefaults.Font.BaseSize, value => theme.Font.BaseSize = value);
        ApplyIfCustomized(legacyTheme.Ui.BorderRadius, frameworkDefaults.Ui.BorderRadius, value => theme.Ui.BorderRadius = value);
        ApplyIfCustomized(legacyTheme.Ui.ButtonStyle, frameworkDefaults.Ui.ButtonStyle, value => theme.Ui.ButtonStyle = value);
        ApplyIfCustomized(legacyTheme.Layout.ProductCardVariant, frameworkDefaults.Layout.ProductCardVariant, value => theme.Layout.ProductCardVariant = value);
        ApplyIfCustomized(legacyTheme.Layout.HeaderVariant, frameworkDefaults.Layout.HeaderVariant, value => theme.Layout.HeaderVariant = value);
    }

    private static void ApplyIfCustomized(string? candidate, string frameworkDefault, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            string.Equals(candidate, frameworkDefault, StringComparison.Ordinal))
        {
            return;
        }

        apply(candidate);
    }

    private static void ApplyThemePreset(StorefrontThemeConfigDto theme, ThemePreset? themePreset, Action<Exception, string>? errorCallback = null)
    {
        if (themePreset == null || string.IsNullOrWhiteSpace(themePreset.BaseThemeJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(themePreset.BaseThemeJson);
            var root = document.RootElement;

            if (root.TryGetProperty("colors", out var colors))
            {
                theme.Colors.Primary = ReadString(colors, "primary", theme.Colors.Primary);
                theme.Colors.Secondary = ReadString(colors, "secondary", theme.Colors.Secondary);
                theme.Colors.Background = ReadString(colors, "background", theme.Colors.Background);
                theme.Colors.Text = ReadString(colors, "text", theme.Colors.Text);
                theme.Colors.Muted = ReadString(colors, "muted", theme.Colors.Muted);
            }

            if (root.TryGetProperty("typography", out var typography))
            {
                theme.Font.Family = ReadString(typography, "fontFamily", theme.Font.Family);
                theme.Font.BaseSize = ReadString(typography, "baseSize", theme.Font.BaseSize);
            }

            if (root.TryGetProperty("radius", out var radius))
            {
                theme.Ui.BorderRadius = ReadString(radius, "base", theme.Ui.BorderRadius);
            }
        }
        catch (Exception ex)
        {
            // Ignore malformed preset JSON and preserve safe defaults.
            errorCallback?.Invoke(ex, $"ApplyThemePreset: {themePreset?.BaseThemeJson}");
        }
    }

    private static void ApplyThemeOverrides(StorefrontThemeConfigDto theme, string? overridesJson, Action<Exception, string>? errorCallback = null)
    {
        if (string.IsNullOrWhiteSpace(overridesJson) || overridesJson == "{}")
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(overridesJson);
            var root = document.RootElement;

            if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
            {
                var primary = ReadString(colors, "primary", theme.Colors.Primary);
                var background = ReadString(colors, "background", theme.Colors.Background);
                var text = ReadString(colors, "text", theme.Colors.Text);
                var secondary = ReadString(colors, "secondary", theme.Colors.Secondary);
                var muted = ReadString(colors, "muted", theme.Colors.Muted);

                theme.Colors.Primary = primary;
                theme.Colors.Background = background;
                theme.Colors.Text = text;
                theme.Colors.Secondary = string.IsNullOrWhiteSpace(secondary) ? text : secondary;
                theme.Colors.Muted = string.IsNullOrWhiteSpace(muted) ? theme.Colors.Muted : muted;
            }

            if (root.TryGetProperty("typography", out var typography) && typography.ValueKind == JsonValueKind.Object)
            {
                theme.Font.Family = ReadString(typography, "fontFamily", theme.Font.Family);
                theme.Font.BaseSize = ReadString(typography, "baseSize", theme.Font.BaseSize);
            }

            if (root.TryGetProperty("font", out var font) && font.ValueKind == JsonValueKind.String)
            {
                theme.Font.Family = font.GetString() ?? theme.Font.Family;
            }

            if (root.TryGetProperty("radius", out var radius) && radius.ValueKind == JsonValueKind.Object)
            {
                theme.Ui.BorderRadius = ReadString(radius, "base", theme.Ui.BorderRadius);
            }

            if (root.TryGetProperty("borderRadius", out var borderRadius))
            {
                if (borderRadius.ValueKind == JsonValueKind.Number)
                {
                    // Try safe int parsing first
                    if (borderRadius.TryGetInt32(out var intVal))
                    {
                        theme.Ui.BorderRadius = $"{intVal}px";
                    }
                    // Fallback to double for out-of-range or decimal values
                    else if (borderRadius.TryGetDouble(out var doubleVal))
                    {
                        theme.Ui.BorderRadius = $"{(int)doubleVal}px";
                    }
                    // If both fail, preserve existing default
                }
                else
                {
                    theme.Ui.BorderRadius = borderRadius.GetString() ?? theme.Ui.BorderRadius;
                }
            }
        }
        catch (Exception ex)
        {
            // Ignore malformed override JSON and preserve safe defaults.
            errorCallback?.Invoke(ex, $"ApplyThemeOverrides: {overridesJson}");
        }
    }

    private static StorefrontPagesDto MapPages(
        string? customizationJson,
        bool useDraftContent,
        Action<Exception, string>? errorCallback = null)
    {
        var pages = new StorefrontPagesDto();

        if (!TryGetRoot(customizationJson, out var root, errorCallback) ||
            !root.TryGetProperty("pages", out var pagesElement) ||
            pagesElement.ValueKind != JsonValueKind.Object)
        {
            return pages;
        }

        pages.Home = MapPage(pagesElement, "home", useDraftContent, errorCallback);
        pages.Product = MapPage(pagesElement, "product", useDraftContent, errorCallback);
        pages.Category = MapPage(pagesElement, "category", useDraftContent, errorCallback);

        return pages;
    }

    private static Dictionary<string, StorefrontProductTemplateDto> MapProductTemplates(
        string? customizationJson,
        bool useDraftContent,
        Action<Exception, string>? errorCallback = null)
    {
        var templates = new Dictionary<string, StorefrontProductTemplateDto>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetRoot(customizationJson, out var root, errorCallback) ||
            !root.TryGetProperty("product_templates", out var templatesElement) ||
            templatesElement.ValueKind != JsonValueKind.Object)
        {
            return templates;
        }

        foreach (var templateNode in templatesElement.EnumerateObject())
        {
            try
            {
                var content = ResolvePublishedContent(templateNode.Value, useDraftContent);
                if (content == null)
                {
                    continue;
                }

                var template = JsonSerializer.Deserialize<StorefrontProductTemplateDto>(content.Value.GetRawText(), JsonOptions);
                if (template == null)
                {
                    continue;
                }

                template.TemplateId = string.IsNullOrWhiteSpace(template.TemplateId)
                    ? templateNode.Name
                    : template.TemplateId;
                template.Name = string.IsNullOrWhiteSpace(template.Name)
                    ? template.TemplateId
                    : template.Name;
                template.Sections = NormalizeSections(
                    template.Sections,
                    errorCallback,
                    $"MapProductTemplates: {templateNode.Name}");

                templates[templateNode.Name] = template;
            }
            catch (Exception ex)
            {
                errorCallback?.Invoke(ex, $"MapProductTemplates: Failed to deserialize template '{templateNode.Name}'");
                // Continue to next template instead of aborting entire mapping
            }
        }

        return templates;
    }

    private static StorefrontPageConfigDto MapPage(
        JsonElement pagesElement,
        string pageKey,
        bool useDraftContent,
        Action<Exception, string>? errorCallback = null)
    {
        if (!pagesElement.TryGetProperty(pageKey, out var pageElement))
        {
            return new StorefrontPageConfigDto();
        }

        try
        {
            var content = ResolvePublishedContent(pageElement, useDraftContent);
            if (content == null)
            {
                return new StorefrontPageConfigDto();
            }

            var page = JsonSerializer.Deserialize<StorefrontPageConfigDto>(content.Value.GetRawText(), JsonOptions)
                ?? new StorefrontPageConfigDto();

            page.Sections = NormalizeSections(page.Sections, errorCallback, $"MapPage: {pageKey}");
            return page;
        }
        catch (Exception ex)
        {
            errorCallback?.Invoke(ex, $"MapPage: Failed to deserialize '{pageKey}' page content");
            return new StorefrontPageConfigDto();
        }
    }

    private static JsonElement? ResolvePublishedContent(JsonElement candidate, bool useDraftContent)
    {
        if (candidate.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return candidate;
        }

        var hasDraft = candidate.TryGetProperty("draft", out var draft);
        var hasLive = candidate.TryGetProperty("live", out var live);

        if (hasDraft || hasLive)
        {
            var selected = useDraftContent ? draft : live;
            var hasSelected = useDraftContent ? hasDraft : hasLive;
            if (!hasSelected || selected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return selected;
        }

        return candidate;
    }

    private static List<StorefrontSectionDto> NormalizeSections(
        List<StorefrontSectionDto>? sections,
        Action<Exception, string>? errorCallback,
        string operation)
    {
        if (sections == null || sections.Count == 0)
        {
            return new List<StorefrontSectionDto>();
        }

        var normalizedSections = new List<StorefrontSectionDto>();
        foreach (var section in sections)
        {
            var normalizedSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in section.Settings ?? new Dictionary<string, object>())
            {
                normalizedSettings[key] = ConvertJsonElement(value);
            }

            var canonicalSectionType = StorefrontSectionContracts.NormalizeSectionType(section.Type);
            (canonicalSectionType, normalizedSettings) = CanonicalizeLegacyBuilderSection(
                canonicalSectionType,
                normalizedSettings);
            if (!StorefrontSectionContracts.TryValidateSettingsStrict(
                    canonicalSectionType,
                    normalizedSettings,
                    out var diagnostics))
            {
                errorCallback?.Invoke(
                    new InvalidOperationException(string.Join(" | ", diagnostics)),
                    $"{operation}: Skipped invalid section '{section.Id}' of type '{section.Type}'");
                continue;
            }

            section.Type = canonicalSectionType;
            section.Settings = StorefrontSectionContracts.GetFields(canonicalSectionType)
                .ToDictionary(
                    field => field.Key,
                    field => normalizedSettings[field.Key]!,
                    StringComparer.OrdinalIgnoreCase);
            normalizedSections.Add(section);
        }

        return normalizedSections
            .OrderBy(section => section.Order)
            .ToList();
    }

    private static (string SectionType, Dictionary<string, object?> Settings) CanonicalizeLegacyBuilderSection(
        string sectionType,
        Dictionary<string, object?> settings)
    {
        var migrated = false;

        if (sectionType.Equals("customer-reviews", StringComparison.OrdinalIgnoreCase))
        {
            sectionType = "testimonials";
            settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = ReadLegacyString(settings, "title", "Customer Testimonials"),
                ["subtitle"] = string.Empty,
                ["testimonials"] = ConvertLegacyReviewsToTestimonials(settings)
            };
            migrated = true;
        }

        if (sectionType.Equals("featured-products", StringComparison.OrdinalIgnoreCase) &&
            !settings.ContainsKey("maxItems") && settings.Remove("limit", out var legacyLimit))
        {
            settings["maxItems"] = legacyLimit;
            migrated = true;
        }

        if (sectionType.Equals("testimonials", StringComparison.OrdinalIgnoreCase) &&
            !settings.ContainsKey("title") && settings.Remove("heading", out var legacyHeading))
        {
            settings["title"] = legacyHeading;
            migrated = true;
        }

        if (sectionType.Equals("testimonials", StringComparison.OrdinalIgnoreCase) &&
            settings.TryGetValue("testimonials", out var testimonialsValue) &&
            testimonialsValue is IEnumerable<object?> testimonials &&
            testimonialsValue is not string &&
            testimonials.OfType<IReadOnlyDictionary<string, object?>>().Any(item => item.ContainsKey("rating")))
        {
            settings["testimonials"] = testimonials
                .OfType<IReadOnlyDictionary<string, object?>>()
                .Select(item => (object?)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = ReadLegacyString(item, "name", string.Empty),
                    ["avatar"] = ReadLegacyString(item, "avatar", string.Empty),
                    ["text"] = ReadLegacyString(item, "text", string.Empty)
                })
                .ToArray();
            migrated = true;
        }

        var obsoleteKeys = sectionType switch
        {
            "product-grid" => new[] { "columns", "rows", "showPrice", "showRating", "showAddToCart" },
            "featured-products" => new[] { "showPrice", "showRating" },
            "category-grid" or "subcategory-grid" => new[] { "showProductCount", "cardStyle" },
            "testimonials" => new[] { "layout", "showRating", "autoplay" },
            "product-hero" => new[] { "showDescription", "showMediaGallery", "showVariantSelector", "showAddToCart", "showSku", "showStockStatus" },
            "product-gallery" => new[] { "showThumbnails", "maxImages" },
            "product-specs" => new[] { "useDescription", "items" },
            "product-video" => new[] { "videoUrl", "autoplay", "showControls" },
            "related-products" => new[] { "showPrice", "showRating" },
            _ => Array.Empty<string>()
        };

        foreach (var obsoleteKey in obsoleteKeys)
        {
            migrated |= settings.Remove(obsoleteKey);
        }

        if (migrated)
        {
            var definition = StorefrontSectionContracts.All.FirstOrDefault(candidate =>
                candidate.Type.Equals(sectionType, StringComparison.OrdinalIgnoreCase));
            if (definition != null)
            {
                foreach (var (key, defaultValue) in definition.DefaultSettings)
                {
                    settings.TryAdd(key, defaultValue);
                }
            }
        }

        return (sectionType, settings);
    }

    private static string ReadLegacyString(
        IReadOnlyDictionary<string, object?> settings,
        string key,
        string fallback) =>
        settings.TryGetValue(key, out var value) && value is string stringValue
            ? stringValue
            : fallback;

    private static IReadOnlyList<object?> ConvertLegacyReviewsToTestimonials(
        IReadOnlyDictionary<string, object?> settings)
    {
        if (!settings.TryGetValue("reviews", out var reviewsValue) ||
            reviewsValue is string ||
            reviewsValue is not IEnumerable<object?> reviews)
        {
            return Array.Empty<object?>();
        }

        return reviews
            .OfType<IReadOnlyDictionary<string, object?>>()
            .Select(review => (object?)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = ReadLegacyString(review, "name", string.Empty),
                ["avatar"] = ReadLegacyString(review, "avatar", string.Empty),
                ["text"] = ReadLegacyString(review, "text", string.Empty)
            })
            .ToArray();
    }

    private static bool TryGetRoot(string? customizationJson, out JsonElement root, Action<Exception, string>? errorCallback = null)
    {
        root = default;

        if (string.IsNullOrWhiteSpace(customizationJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(customizationJson);
            root = document.RootElement.Clone();
            return true;
        }
        catch (Exception ex)
        {
            errorCallback?.Invoke(ex, $"TryGetRoot: {customizationJson}");
            return false;
        }
    }

    private static string ReadString(JsonElement parent, string propertyName, string fallback)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static object? ConvertJsonElement(object? value)
    {
        if (value is not JsonElement jsonElement)
        {
            return value;
        }

        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt64(out var intValue) ? intValue : jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(item => ConvertJsonElement(item)).ToList(),
            JsonValueKind.Object => jsonElement
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
            _ => jsonElement.ToString()
        };
    }
}
