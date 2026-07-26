import React from 'react';
import type { ProductListItemDto } from '@/types';
import { ProductCardDetailed } from './ProductCardDetailed';

interface ProductCardFashionProps {
    product: ProductListItemDto;
}

export const ProductCardFashion: React.FC<ProductCardFashionProps> = ({ product }) => (
    <ProductCardDetailed product={product} />
);

export default ProductCardFashion;
