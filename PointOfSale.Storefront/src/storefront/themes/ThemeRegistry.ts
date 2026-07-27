export interface ThemeDefinition {
    id: string;
    name: string;
    version: string;
    capabilities: readonly string[];
    isDefault?: boolean;
}

const THEME_CAPABILITIES = [
    'layout.header',
    'layout.footer',
    'section.hero-banner',
    'section.product-grid',
    'section.category-grid',
] as const;

const AVAILABLE_THEMES: ThemeDefinition[] = validateThemeRegistry([
    {
        id: 'modern',
        name: 'Modern',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
        isDefault: true,
    },
    {
        id: 'minimal',
        name: 'Minimal',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
    },
    {
        id: 'fashion',
        name: 'Fashion',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
    },
    {
        id: 'pearl',
        name: 'Pearl',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
    },
]);

export function getAvailableThemes(): ThemeDefinition[] {
    return AVAILABLE_THEMES;
}

export function getDefaultTheme(): ThemeDefinition {
    const defaultTheme = AVAILABLE_THEMES.find((theme) => theme.isDefault);

    if (!defaultTheme) {
        throw new Error('[ThemeRegistry] No default theme is configured.');
    }

    return defaultTheme;
}

function validateThemeRegistry(themes: ThemeDefinition[]): ThemeDefinition[] {
    const seen = new Set<string>();
    const defaults = themes.filter((theme) => theme.isDefault);

    if (defaults.length !== 1) {
        throw new Error('[ThemeRegistry] Exactly one default theme must be configured.');
    }

    for (const theme of themes) {
        const normalizedId = theme.id.trim().toLowerCase();

        if (!normalizedId || seen.has(normalizedId)) {
            throw new Error(`[ThemeRegistry] Duplicate or empty theme id: "${theme.id}".`);
        }

        if (!theme.name.trim() || !theme.version.trim() || theme.capabilities.length === 0) {
            throw new Error(`[ThemeRegistry] Theme "${theme.id}" has an invalid manifest.`);
        }

        seen.add(normalizedId);
    }

    return themes;
}
