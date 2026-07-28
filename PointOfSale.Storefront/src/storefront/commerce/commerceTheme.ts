import type { ThemeComponentTheme } from '@/components/theme-components/themeComponentTypes';
import themeAuthority from '@theme-authority/shopixy-theme-authority.json';

const COMMERCE_THEME_BY_THEME_ID = new Map(
    themeAuthority.themes.map((theme) => [theme.id, theme.commerceBrowsingTheme])
);

export function normalizeCommerceTheme(themeId?: string | null): ThemeComponentTheme {
    return (COMMERCE_THEME_BY_THEME_ID.get(`${themeId ?? ''}`.trim().toLowerCase()) as ThemeComponentTheme | undefined) ?? 'default';
}
