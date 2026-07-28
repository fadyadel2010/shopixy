import React from 'react';
import type { ProductListItemDto } from '@/types';
import { FashionProductCardRenderer } from './ProductCardRenderers';
import { useProductCardRuntime } from './ProductCardRuntime';

interface ProductCardFashionProps {
    product: ProductListItemDto;
}

export const ProductCardFashion: React.FC<ProductCardFashionProps> = ({ product }) => {
    const runtime = useProductCardRuntime({ product, layout: 'fashion', themeId: 'fashion' });
    return <FashionProductCardRenderer runtime={runtime} />;
};

export default ProductCardFashion;
