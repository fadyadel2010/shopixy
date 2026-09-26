import themeAuthority from '@theme-authority/shopixy-theme-authority.json'

const THEME_MARKETPLACE_REGISTRY = validateThemeMarketplaceRegistry(
    themeAuthority.themes
        .filter((theme) => theme.officialStatus === 'official' && theme.marketplaceVisibility === 'visible')
        .map((theme) => ({
            id: theme.id,
            name: theme.displayName,
            version: theme.version,
            capabilities: theme.capabilities,
            isDefault: theme.id === themeAuthority.defaultThemeId,
            previewImage: theme.marketplace.previewImage,
            description: theme.marketplace.description,
            accent: theme.marketplace.accent,
        }))
)

export function getAvailableMarketplaceThemes() {
    return THEME_MARKETPLACE_REGISTRY
}

export function getMarketplaceThemeById(themeId) {
    return THEME_MARKETPLACE_REGISTRY.find((theme) => theme.id === themeId)
}

export function getDefaultMarketplaceTheme() {
    return THEME_MARKETPLACE_REGISTRY.find((theme) => theme.isDefault)
}

export function getThemeArtworkStyle(theme) {
    return { backgroundColor: theme.accent }
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
