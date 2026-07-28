import React from 'react';
import { ProductMediaGallery } from '@/components/products';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { useTheme } from '@/storefront/themes/ThemeContext';
import type { ProductReadDto } from '@/types';

interface ProductGalleryProps {
    product?: ProductReadDto | null;
    title?: string;
    showThumbnails?: boolean;
    maxImages?: number;
    layoutVariant?: string;
}

const ProductGallery: React.FC<ProductGalleryProps> = ({
    product,
    title,
    showThumbnails = true,
    maxImages = 6,
    layoutVariant = 'default',
}) => {
    const { themeId } = useTheme();
    const profile = getPurchaseExperienceProfile(layoutVariant, themeId);

    if (!product) {
        return null;
    }

    return (
        <section className={`${profile.sectionClassName} px-4 py-8`} data-purchase-experience={profile.variant}>
            <div className="container mx-auto">
                {title ? <h2 className="mb-6 text-2xl font-bold text-text">{title}</h2> : null}

                <ProductMediaGallery
                    product={product}
                    showThumbnails={showThumbnails}
                    maxThumbnails={Math.max(1, Number(maxImages || 6))}
                    aspectClassName={profile.galleryAspectClassName}
                    presentationVariant={profile.variant}
                />
            </div>
        </section>
    );
};

export default ProductGallery;
