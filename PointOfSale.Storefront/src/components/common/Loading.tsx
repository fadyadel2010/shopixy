/**
 * Loading Skeleton Component
 * 
 * Graceful loading skeletons for various content types.
 */

import React from 'react';

interface SkeletonProps {
    className?: string;
}

export const Skeleton: React.FC<SkeletonProps> = ({ className = '' }) => {
    return (
        <div className={`animate-pulse bg-muted/30 rounded ${className}`} />
    );
};

/**
 * Product Card Skeleton
 */
export const ProductCardSkeleton: React.FC = () => {
    return (
        <div className="bg-background border border-muted/20 rounded p-4">
            <Skeleton className="w-full h-48 mb-4" />
            <Skeleton className="w-3/4 h-4 mb-2" />
            <Skeleton className="w-1/2 h-4 mb-4" />
            <Skeleton className="w-1/4 h-6" />
        </div>
    );
};

/**
 * Product Grid Skeleton
 */
export const ProductGridSkeleton: React.FC<{ count?: number }> = ({ count = 8 }) => {
    return (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-6">
            {Array.from({ length: count }).map((_, i) => (
                <ProductCardSkeleton key={i} />
            ))}
        </div>
    );
};

/**
 * Product Detail Skeleton
 */
export const ProductDetailSkeleton: React.FC = () => {
    return (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
            <Skeleton className="w-full h-96" />
            <div className="space-y-4">
                <Skeleton className="w-3/4 h-8" />
                <Skeleton className="w-1/4 h-6" />
                <Skeleton className="w-full h-24" />
                <Skeleton className="w-1/3 h-12" />
            </div>
        </div>
    );
};

/**
 * Full Page Loading
 */
export const PageLoading: React.FC = () => {
    return (
        <div className="flex items-center justify-center min-h-[400px]">
            <div className="text-center">
                <div className="inline-block w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin" />
                <p className="mt-4 text-muted">Loading...</p>
            </div>
        </div>
    );
};
