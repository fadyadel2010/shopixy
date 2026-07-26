import React from 'react';
import type { ProductListItemDto } from '@/types';
import { ProductCardDefault } from './ProductCardDefault';

interface ProductCardElectronicsProps {
    product: ProductListItemDto;
}

export const ProductCardElectronics: React.FC<ProductCardElectronicsProps> = ({ product }) => (
    <ProductCardDefault product={product} />
);

export default ProductCardElectronics;
