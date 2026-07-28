import React from 'react';
import type { ProductListItemDto } from '@/types';
import { ElectronicsProductCardRenderer } from './ProductCardRenderers';
import { useProductCardRuntime } from './ProductCardRuntime';

interface ProductCardElectronicsProps {
    product: ProductListItemDto;
}

export const ProductCardElectronics: React.FC<ProductCardElectronicsProps> = ({ product }) => {
    const runtime = useProductCardRuntime({ product, layout: 'electronics', themeId: 'modern' });
    return <ElectronicsProductCardRenderer runtime={runtime} />;
};

export default ProductCardElectronics;
