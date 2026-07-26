/**
 * Product Card Component
 * 
 * Main product card that switches between variants based on StoreConfig.
 * Uses component composition for variants - NOT conditionals scattered everywhere.
 * 
 * Variants:
 * - default: Standard card with balanced image and content
 * - compact: Smaller, horizontal layout for dense grids
 * - detailed: Rich card with additional information
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { useStoreConfigStore, selectLayout } from '@/store';
import {
    ProductCardDefault,
    ProductCardCompact,
    ProductCardDetailed,
    ProductCardFashion,
    ProductCardElectronics,
} from '@/components/products';
import { resolveProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';

interface ProductCardProps {
    product: ProductListItemDto;
}

/**
 * Product card variants mapping
 */
const PRODUCT_CARD_VARIANTS = {
    default: ProductCardDefault,
    compact: ProductCardCompact,
    detailed: ProductCardDetailed,
    fashion: ProductCardFashion,
    electronics: ProductCardElectronics,
} as const;

/**
 * Main ProductCard Component
 * Uses component composition to switch variants based on storeConfig
 */
export const ProductCard: React.FC<ProductCardProps> = ({ product }) => {
    const layout = useStoreConfigStore(selectLayout);
    const resolvedLayout = resolveProductCardLayout(product, layout.productCardVariant);

    // Product-level storefront layout wins. Theme layout is the fallback.
    const CardComponent = PRODUCT_CARD_VARIANTS[resolvedLayout] || ProductCardDefault;

    // Log variant in development mode
    if (import.meta.env.DEV) {
        console.log('[ProductCard] Layout:', resolvedLayout, 'Theme fallback:', layout.productCardVariant);
    }

    return (
        <>
            <div data-ai-product-id={product.id} hidden aria-hidden="true" />
            <CardComponent product={product} />
        </>
    );
};
