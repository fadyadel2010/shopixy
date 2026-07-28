/**
 * Product Card - Default Variant
 * 
 * Standard product card with balanced image and content display.
 * Uses CSS variables for all theming.
 * Phase 10.1: Multi-language support
 * Phase 10.3.6: Wishlist button integration
 * Phase: Promotions Engine - Badge display
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { ModernProductCardRenderer } from './ProductCardRenderers';
import { useProductCardRuntime } from './ProductCardRuntime';

interface ProductCardDefaultProps {
    product: ProductListItemDto;
}

export const ProductCardDefault: React.FC<ProductCardDefaultProps> = ({ product }) => {
    const runtime = useProductCardRuntime({ product, layout: 'default', themeId: 'modern' });
    return <ModernProductCardRenderer runtime={runtime} />;
};
