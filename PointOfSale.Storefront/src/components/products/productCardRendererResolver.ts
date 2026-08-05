import themeAuthority from '@theme-authority/shopixy-theme-authority.json';

export type ProductCardRendererId =
    | 'modern'
    | 'minimal'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics'
    | 'alizon'
    | 'sharp'
    | 'cosmetics'
    | 'seven';

const THEME_RENDERERS = new Map(
    themeAuthority.themes
        .filter((theme) => theme.officialStatus === 'official' && theme.activationStatus === 'active')
        .map((theme) => [theme.id, theme.supportedProductCards[0]])
);

export function resolveProductCardRenderer(
    themeId: string | null | undefined
): ProductCardRendererId {
    const normalizedThemeId = `${themeId ?? ''}`.trim().toLowerCase();

    return (THEME_RENDERERS.get(normalizedThemeId) as ProductCardRendererId | undefined) ?? 'modern';
}
