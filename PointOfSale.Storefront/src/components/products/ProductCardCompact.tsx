/**
 * Product Card - Compact Variant
 * 
 * Smaller, condensed product card for dense product grids.
 * Optimized for mobile and space-constrained layouts.
 * Uses CSS variables for all theming.
 * Phase 10.1: Multi-language support
 * Phase 10.3.6: Wishlist button integration
 * Phase: Promotions Engine - Badge display
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { MinimalProductCardRenderer } from './ProductCardRenderers';
import { useProductCardRuntime } from './ProductCardRuntime';

interface ProductCardCompactProps {
    product: ProductListItemDto;
}

export const ProductCardCompact: React.FC<ProductCardCompactProps> = ({ product }) => {
    const runtime = useProductCardRuntime({ product, layout: 'compact', themeId: 'minimal' });
    return <MinimalProductCardRenderer runtime={runtime} />;
};
