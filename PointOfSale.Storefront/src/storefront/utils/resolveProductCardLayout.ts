import type { LayoutConfig } from '@/types/storeConfig';

export type ProductCardLayout =
    | 'default'
    | 'compact'
    | 'detailed'
    | 'fashion'
    | 'pearl'
    | 'phoenix'
    | 'hype'
    | 'electronics';

function mapThemeVariantToCardLayout(themeVariant?: LayoutConfig['productCardVariant']): ProductCardLayout {
    switch (themeVariant) {
        case 'compact':
            return 'compact';
        case 'detailed':
            return 'detailed';
        default:
            return 'default';
    }
}

export function resolveProductCardLayout(
    themeVariant?: LayoutConfig['productCardVariant']
): ProductCardLayout {
    return mapThemeVariantToCardLayout(themeVariant);
}

export default resolveProductCardLayout;
