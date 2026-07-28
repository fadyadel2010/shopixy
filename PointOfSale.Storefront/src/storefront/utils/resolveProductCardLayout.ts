import type { ProductListItemDto } from '@/types';
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

function normalizeLayoutValue(value?: string | null): string {
    return `${value ?? ''}`.trim().toLowerCase();
}

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
    product: Pick<ProductListItemDto, 'storefrontLayout' | 'productTemplate'> | null | undefined,
    themeVariant?: LayoutConfig['productCardVariant']
): ProductCardLayout {
    const productLevelLayout = normalizeLayoutValue(product?.storefrontLayout || product?.productTemplate);

    switch (productLevelLayout) {
        case 'fashion':
            return 'fashion';
        case 'pearl':
            return 'pearl';
        case 'phoenix':
            return 'phoenix';
        case 'hype':
            return 'hype';
        case 'electronics':
            return 'electronics';
        case 'default':
            return 'default';
        default:
            return mapThemeVariantToCardLayout(themeVariant);
    }
}

export default resolveProductCardLayout;
