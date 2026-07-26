import { getAvailableThemes, getDefaultTheme, type ThemeDefinition } from './ThemeRegistry';

const STOREFRONT_THEME_STORAGE_KEY = 'storefront_theme';

function findThemeById(themeId: string | null): ThemeDefinition | undefined {
    if (!themeId) {
        return undefined;
    }

    return getAvailableThemes().find((theme) => theme.id === themeId);
}

function isDevOverrideEnabled(): boolean {
    return import.meta.env.DEV && import.meta.env.VITE_ENABLE_STOREFRONT_THEME_DEV_OVERRIDE === 'true';
}

function readDevOverrideThemeId(): string | null {
    if (!isDevOverrideEnabled() || typeof window === 'undefined') {
        return null;
    }

    try {
        return window.localStorage.getItem(STOREFRONT_THEME_STORAGE_KEY);
    } catch {
        return null;
    }
}

export function resolveActiveTheme(storefrontTheme?: string | null): ThemeDefinition {
    const devOverrideThemeId = readDevOverrideThemeId();
    const matchedDevOverrideTheme = findThemeById(devOverrideThemeId);
    const matchedStoreConfigTheme = findThemeById(storefrontTheme ?? null);
    const resolvedTheme =
        matchedDevOverrideTheme ??
        matchedStoreConfigTheme ??
        getDefaultTheme();

    if (import.meta.env.DEV) {
        console.log('[ThemeResolver] Theme resolved:', {
            devOverrideTheme: devOverrideThemeId,
            devOverrideEnabled: isDevOverrideEnabled(),
            storefrontTheme,
            resolvedTheme: resolvedTheme.id,
            source: matchedDevOverrideTheme
                    ? 'devOverride'
                    : matchedStoreConfigTheme
                        ? 'storeConfig'
                        : 'default',
        });
    }

    return resolvedTheme;
}

export function getActiveThemeId(storefrontTheme?: string | null): string {
    return resolveActiveTheme(storefrontTheme).id;
}
