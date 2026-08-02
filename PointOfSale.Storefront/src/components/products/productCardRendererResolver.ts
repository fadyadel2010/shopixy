import type { ProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';
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
    themeId: string | null | undefined,
    layout: ProductCardLayout
): ProductCardRendererId {
    const normalizedThemeId = `${themeId ?? ''}`.trim().toLowerCase();

    if (layout === 'electronics' && normalizedThemeId === 'electronics') {
        return 'electronics';
    }

    if (layout === 'hype' && normalizedThemeId === 'hype') {
        return 'hype';
    }

    if (layout === 'phoenix' && normalizedThemeId === 'phoenix') {
        return 'phoenix';
    }

    if (layout === 'pearl' && normalizedThemeId === 'pearl') {
        return 'pearl';
    }

    if (layout === 'fashion' && normalizedThemeId === 'fashion') {
        return 'fashion';
    }

    return (THEME_RENDERERS.get(normalizedThemeId) as ProductCardRendererId | undefined) ?? 'modern';
}
