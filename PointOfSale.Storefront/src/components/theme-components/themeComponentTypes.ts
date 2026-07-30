import type React from 'react';
import themeAuthority from '@theme-authority/shopixy-theme-authority.json';

export type ThemeComponentTheme =
    | 'modern'
    | 'minimal'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics'
    | 'alizon'
    | 'sharp'
    | 'seven'
    | 'default';

export type ThemePricePresentation =
    | { kind: 'text'; text: string }
    | { kind: 'sale'; current: string; compareAt: string }
    | { kind: 'regular'; current: string };

export type ThemeComponentAlign = 'left' | 'center' | 'right';

export interface ThemeComponentAction {
    label: string;
    href?: string;
    onClick?: React.MouseEventHandler<HTMLButtonElement>;
}

const COMPONENT_THEME_BY_THEME_ID = new Map(
    themeAuthority.themes.map((theme) => [theme.id, theme.componentTheme])
);

export function normalizeThemeComponentTheme(themeId?: string | null): ThemeComponentTheme {
    return (COMPONENT_THEME_BY_THEME_ID.get(`${themeId ?? ''}`.trim().toLowerCase()) as ThemeComponentTheme | undefined) ?? 'modern';
}
