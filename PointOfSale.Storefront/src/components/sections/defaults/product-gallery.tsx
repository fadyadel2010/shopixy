import React from 'react';
import { ProductMediaGallery } from '@/components/products';
import type { ProductReadDto } from '@/types';

interface ProductGalleryProps {
    product?: ProductReadDto | null;
    title?: string;
    showThumbnails?: boolean;
    maxImages?: number;
}

const ProductGallery: React.FC<ProductGalleryProps> = ({
    product,
    title,
    showThumbnails = true,
    maxImages = 6,
}) => {
    if (!product) {
        return null;
    }

    return (
        <section className="container mx-auto px-4 py-8">
            <div className="rounded-3xl border border-muted/20 bg-background p-6 shadow-sm">
                {title ? <h2 className="mb-6 text-2xl font-bold text-text">{title}</h2> : null}

                <ProductMediaGallery
                    product={product}
                    showThumbnails={showThumbnails}
                    maxThumbnails={Math.max(1, Number(maxImages || 6))}
                    aspectClassName="aspect-[4/3]"
                />
            </div>
        </section>
    );
};

export default ProductGallery;
