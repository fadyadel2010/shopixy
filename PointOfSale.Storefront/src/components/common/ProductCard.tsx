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
import { PRODUCT_CARD_RENDERERS, useProductCardRuntime } from '@/components/products';
import { resolveProductCardRenderer } from '@/components/products/productCardRendererResolver';
import { resolveProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';
import { useTheme } from '@/storefront/themes/ThemeContext';

interface ProductCardProps {
    product: ProductListItemDto;
}

/**
 * Main ProductCard Component
 * Uses component composition to switch variants based on storeConfig
 */
export const ProductCard: React.FC<ProductCardProps> = ({ product }) => {
    const layout = useStoreConfigStore(selectLayout);
    const { themeId } = useTheme();
    const resolvedLayout = resolveProductCardLayout(product, layout.productCardVariant);
    const rendererId = resolveProductCardRenderer(themeId, resolvedLayout);
    const runtime = useProductCardRuntime({ product, layout: resolvedLayout, themeId });

    const CardComponent = PRODUCT_CARD_RENDERERS[rendererId] || PRODUCT_CARD_RENDERERS.modern;

    // Log variant in development mode
    if (import.meta.env.DEV) {
        console.log('[ProductCard] Layout:', resolvedLayout, 'Theme:', themeId, 'Renderer:', rendererId);
    }

    return (
        <>
            <div data-ai-product-id={product.id} hidden aria-hidden="true" />
            <CardComponent runtime={runtime} />
        </>
    );
};
