/**
 * Skeleton Grid Component
 * 
 * Displays a grid of skeleton cards for loading states.
 * Respects product card variant from StoreConfig.
 */

import React from 'react';
import { useStoreConfigStore, selectLayout } from '@/store';

interface SkeletonGridProps {
    /** Number of skeleton items to display */
    count?: number;
    /** Override the variant (default uses StoreConfig) */
    variant?: 'default' | 'compact' | 'detailed';
}

/**
 * Single skeleton card - default variant
 */
const SkeletonCardDefault: React.FC = () => (
    <div className="bg-background border border-muted/20 rounded-lg overflow-hidden">
        {/* Image skeleton */}
        <div className="aspect-square bg-muted/10 skeleton" />

        {/* Content skeleton */}
        <div className="p-4 space-y-3">
            <div className="h-3 w-1/3 bg-muted/10 rounded skeleton" />
            <div className="h-4 w-full bg-muted/10 rounded skeleton" />
            <div className="h-4 w-2/3 bg-muted/10 rounded skeleton" />
            <div className="h-5 w-1/4 bg-muted/10 rounded skeleton mt-2" />
        </div>
    </div>
);

/**
 * Single skeleton card - compact variant
 */
const SkeletonCardCompact: React.FC = () => (
    <div className="flex gap-3 bg-background border border-muted/15 rounded-md p-3">
        {/* Image skeleton */}
        <div className="w-20 h-20 flex-shrink-0 bg-muted/10 rounded skeleton" />

        {/* Content skeleton */}
        <div className="flex-1 space-y-2 py-1">
            <div className="h-4 w-full bg-muted/10 rounded skeleton" />
            <div className="h-3 w-1/2 bg-muted/10 rounded skeleton" />
            <div className="h-4 w-1/3 bg-muted/10 rounded skeleton mt-2" />
        </div>
    </div>
);

/**
 * Single skeleton card - detailed variant
 */
const SkeletonCardDetailed: React.FC = () => (
    <div className="bg-background border border-muted/20 rounded-xl overflow-hidden">
        {/* Image skeleton */}
        <div className="aspect-[4/3] bg-muted/10 skeleton" />

        {/* Content skeleton */}
        <div className="p-5 space-y-3">
            <div className="h-5 w-full bg-muted/10 rounded skeleton" />
            <div className="h-5 w-2/3 bg-muted/10 rounded skeleton" />
            <div className="h-4 w-1/2 bg-muted/10 rounded skeleton" />

            {/* Price and button row */}
            <div className="flex items-center justify-between pt-3 border-t border-muted/10">
                <div className="h-6 w-1/4 bg-muted/10 rounded skeleton" />
                <div className="h-10 w-24 bg-muted/10 rounded-lg skeleton" />
            </div>
        </div>
    </div>
);

/**
 * Skeleton variants mapping
 */
const SKELETON_VARIANTS = {
    default: SkeletonCardDefault,
    compact: SkeletonCardCompact,
    detailed: SkeletonCardDetailed,
} as const;

/**
 * Grid class based on variant
 */
const GRID_CLASSES = {
    default: 'grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4 md:gap-6',
    compact: 'grid grid-cols-1 md:grid-cols-2 gap-3',
    detailed: 'grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4 md:gap-6',
} as const;

/**
 * Skeleton Grid Component
 */
export const SkeletonGrid: React.FC<SkeletonGridProps> = ({
    count = 8,
    variant: variantOverride,
}) => {
    const layout = useStoreConfigStore(selectLayout);
    const variant = variantOverride || layout.productCardVariant;

    const SkeletonCard = SKELETON_VARIANTS[variant] || SkeletonCardDefault;
    const gridClass = GRID_CLASSES[variant] || GRID_CLASSES.default;

    return (
        <div className={gridClass} role="status" aria-label="Loading products">
            {Array.from({ length: count }).map((_, index) => (
                <SkeletonCard key={index} />
            ))}
            <span className="sr-only">Loading products...</span>
        </div>
    );
};
