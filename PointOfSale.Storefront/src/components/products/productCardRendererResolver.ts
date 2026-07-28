import type { ProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';
import themeAuthority from '@theme-authority/shopixy-theme-authority.json';

export type ProductCardRendererId =
    | 'modern'
    | 'minimal'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics';

const THEME_RENDERERS = new Map(
    themeAuthority.themes
        .filter((theme) => theme.officialStatus === 'official' && theme.activationStatus === 'active')
        .map((theme) => [theme.id, theme.supportedProductCards[0]])
);

export function resolveProductCardRenderer(
    themeId: string | null | undefined,
    layout: ProductCardLayout
): ProductCardRendererId {
    if (layout === 'electronics') {
        return 'electronics';
    }

    if (layout === 'hype') {
        return 'hype';
    }

    if (layout === 'phoenix') {
        return 'phoenix';
    }

    if (layout === 'pearl') {
        return 'pearl';
    }

    if (layout === 'fashion') {
        return 'fashion';
    }

    return (THEME_RENDERERS.get(`${themeId ?? ''}`.trim().toLowerCase()) as ProductCardRendererId | undefined) ?? 'modern';
}
