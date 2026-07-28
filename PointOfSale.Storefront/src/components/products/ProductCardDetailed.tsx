/**
 * Product Card - Detailed Variant
 * 
 * Rich product card with additional information display.
 * Shows more product details like description and additional metadata.
 * Uses CSS variables for all theming.
 * Phase 10.1: Multi-language support
 * Phase 10.3.6: Wishlist button integration
 * Phase: Promotions Engine - Badge display
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { FashionProductCardRenderer } from './ProductCardRenderers';
import { useProductCardRuntime } from './ProductCardRuntime';

interface ProductCardDetailedProps {
    product: ProductListItemDto;
}

export const ProductCardDetailed: React.FC<ProductCardDetailedProps> = ({ product }) => {
    const runtime = useProductCardRuntime({ product, layout: 'detailed', themeId: 'fashion' });
    return <FashionProductCardRenderer runtime={runtime} />;
};
