const THEME_MARKETPLACE_REGISTRY = [
    {
        id: 'modern',
        name: 'Modern',
        previewImage: null,
        description: 'Bold, editorial cards with stronger imagery and motion.',
        accent: 'from-slate-900 via-slate-700 to-cyan-400',
    },
    {
        id: 'minimal',
        name: 'Minimal',
        previewImage: null,
        description: 'Quiet layouts, clean borders, and generous white space.',
        accent: 'from-stone-100 via-white to-stone-300',
    },
    {
        id: 'fashion',
        name: 'Fashion',
        previewImage: null,
        description: 'Image-led storytelling with overlay typography.',
        accent: 'from-rose-950 via-fuchsia-800 to-amber-300',
    },
]

export function getAvailableMarketplaceThemes() {
    return THEME_MARKETPLACE_REGISTRY
}

export function getMarketplaceThemeById(themeId) {
    return THEME_MARKETPLACE_REGISTRY.find((theme) => theme.id === themeId)
}

export function getDefaultMarketplaceTheme() {
    return THEME_MARKETPLACE_REGISTRY[0]
}
