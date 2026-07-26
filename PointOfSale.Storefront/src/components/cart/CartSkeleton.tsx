/**
 * Cart Skeleton Component
 * 
 * Skeleton loading state for cart page.
 */

import React from 'react';

interface CartSkeletonProps {
    /** Number of skeleton rows */
    rows?: number;
}

/**
 * Single skeleton row
 */
const SkeletonRow: React.FC = () => (
    <div className="flex gap-4 p-4 border-b border-muted/10">
        {/* Image skeleton */}
        <div className="w-20 h-20 bg-muted/10 rounded-lg skeleton flex-shrink-0" />

        {/* Content skeleton */}
        <div className="flex-1 space-y-2 py-1">
            <div className="h-4 w-3/4 bg-muted/10 rounded skeleton" />
            <div className="h-3 w-1/2 bg-muted/10 rounded skeleton" />
            <div className="h-4 w-1/4 bg-muted/10 rounded skeleton" />
        </div>

        {/* Quantity skeleton */}
        <div className="hidden sm:flex items-center gap-2">
            <div className="w-24 h-10 bg-muted/10 rounded-lg skeleton" />
        </div>

        {/* Price skeleton */}
        <div className="w-20 text-right">
            <div className="h-5 w-full bg-muted/10 rounded skeleton ml-auto" />
        </div>
    </div>
);

/**
 * Cart Skeleton Component
 */
export const CartSkeleton: React.FC<CartSkeletonProps> = ({ rows = 3 }) => {
    return (
        <div role="status" aria-label="Loading cart">
            {/* Header skeleton */}
            <div className="h-8 w-32 bg-muted/10 rounded skeleton mb-6" />

            {/* Items skeleton */}
            <div className="bg-background border border-muted/10 rounded-xl overflow-hidden mb-6">
                {Array.from({ length: rows }).map((_, i) => (
                    <SkeletonRow key={i} />
                ))}
            </div>

            {/* Summary skeleton */}
            <div className="bg-background border border-muted/10 rounded-xl p-6 space-y-4">
                <div className="flex justify-between">
                    <div className="h-4 w-20 bg-muted/10 rounded skeleton" />
                    <div className="h-4 w-24 bg-muted/10 rounded skeleton" />
                </div>
                <div className="flex justify-between">
                    <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                    <div className="h-4 w-20 bg-muted/10 rounded skeleton" />
                </div>
                <div className="h-px bg-muted/10" />
                <div className="flex justify-between">
                    <div className="h-6 w-24 bg-muted/10 rounded skeleton" />
                    <div className="h-6 w-28 bg-muted/10 rounded skeleton" />
                </div>
                <div className="h-12 w-full bg-muted/10 rounded-lg skeleton mt-4" />
            </div>

            <span className="sr-only">Loading cart...</span>
        </div>
    );
};
