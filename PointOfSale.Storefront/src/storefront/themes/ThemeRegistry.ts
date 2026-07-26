export interface ThemeDefinition {
    id: string;
    name: string;
    isDefault?: boolean;
}

const AVAILABLE_THEMES: ThemeDefinition[] = [
    {
        id: 'modern',
        name: 'Modern',
        isDefault: true,
    },
    {
        id: 'minimal',
        name: 'Minimal',
    },
    {
        id: 'fashion',
        name: 'Fashion',
    },
];

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
