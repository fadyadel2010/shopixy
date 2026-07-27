const THEME_CAPABILITIES = [
    'layout.header',
    'layout.footer',
    'section.hero-banner',
    'section.product-grid',
    'section.category-grid',
]

const THEME_MARKETPLACE_REGISTRY = validateThemeMarketplaceRegistry([
    {
        id: 'modern',
        name: 'Modern',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
        isDefault: true,
        previewImage: null,
        description: 'Bold, editorial cards with stronger imagery and motion.',
        accent: 'from-slate-900 via-slate-700 to-cyan-400',
    },
    {
        id: 'minimal',
        name: 'Minimal',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
        previewImage: null,
        description: 'Quiet layouts, clean borders, and generous white space.',
        accent: 'from-stone-100 via-white to-stone-300',
    },
    {
        id: 'fashion',
        name: 'Fashion',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
        previewImage: null,
        description: 'Image-led storytelling with overlay typography.',
        accent: 'from-rose-950 via-fuchsia-800 to-amber-300',
    },
    {
        id: 'pearl',
        name: 'Pearl',
        version: '1.0.0',
        capabilities: THEME_CAPABILITIES,
        previewImage: null,
        description: 'Editorial luxury boutique: light canvas, dark hero blocks, red accent, gold footer.',
        accent: 'from-neutral-900 via-stone-800 to-red-500',
    },
])

export function getAvailableMarketplaceThemes() {
    return THEME_MARKETPLACE_REGISTRY
}

export function getMarketplaceThemeById(themeId) {
    return THEME_MARKETPLACE_REGISTRY.find((theme) => theme.id === themeId)
}

export function getDefaultMarketplaceTheme() {
    return THEME_MARKETPLACE_REGISTRY.find((theme) => theme.isDefault)
}

function validateThemeMarketplaceRegistry(themes) {
    const seen = new Set()
    const defaults = themes.filter((theme) => theme.isDefault)

    if (defaults.length !== 1) {
        throw new Error('[ThemeMarketplaceRegistry] Exactly one default theme must be configured.')
    }

    for (const theme of themes) {
        const normalizedId = typeof theme.id === 'string'
            ? theme.id.trim().toLowerCase()
            : ''

        if (!normalizedId || seen.has(normalizedId)) {
            throw new Error(`[ThemeMarketplaceRegistry] Duplicate or empty theme id: "${theme.id}".`)
        }

        if (!theme.name?.trim() || !theme.version?.trim() || !Array.isArray(theme.capabilities) || theme.capabilities.length === 0) {
            throw new Error(`[ThemeMarketplaceRegistry] Theme "${theme.id}" has an invalid manifest.`)
        }

        seen.add(normalizedId)
    }

    return themes
}
