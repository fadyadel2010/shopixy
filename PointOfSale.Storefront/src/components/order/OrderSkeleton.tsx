/**
 * Order Skeleton Component
 * 
 * Loading skeleton for order pages.
 * Matches the layout of order details.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';

// ============================================================================
// Timeline Skeleton
// ============================================================================

export const OrderTimelineSkeleton: React.FC = () => {
    return (
        <div className="animate-pulse">
            {/* Desktop Timeline */}
            <div className="hidden md:flex items-center justify-between">
                {[1, 2, 3, 4, 5].map((i) => (
                    <React.Fragment key={i}>
                        <div className="flex flex-col items-center">
                            <div className="w-12 h-12 rounded-full bg-muted/10 skeleton" />
                            <div className="h-4 w-16 bg-muted/10 rounded skeleton mt-3" />
                        </div>
                        {i < 5 && (
                            <div className="flex-1 h-1 mx-2 bg-muted/10 skeleton rounded-full" />
                        )}
                    </React.Fragment>
                ))}
            </div>

            {/* Mobile Timeline */}
            <div className="md:hidden space-y-6">
                {[1, 2, 3, 4].map((i) => (
                    <div key={i} className="flex gap-4">
                        <div className="w-12 h-12 rounded-full bg-muted/10 skeleton flex-shrink-0" />
                        <div className="flex-1 pt-2 space-y-2">
                            <div className="h-5 w-24 bg-muted/10 rounded skeleton" />
                            <div className="h-4 w-40 bg-muted/10 rounded skeleton" />
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};

// ============================================================================
// Summary Card Skeleton
// ============================================================================

export const OrderSummaryCardSkeleton: React.FC = () => {
    return (
        <div className="bg-background border border-muted/10 rounded-xl overflow-hidden animate-pulse">
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <div className="h-6 w-32 bg-muted/10 rounded skeleton" />
            </div>

            {/* Content */}
            <div className="p-5 space-y-4">
                {/* Order Number */}
                <div className="pb-3 border-b border-muted/10">
                    <div className="h-3 w-20 bg-muted/10 rounded skeleton mb-2" />
                    <div className="h-6 w-32 bg-muted/10 rounded skeleton" />
                </div>

                {/* Rows */}
                {[1, 2, 3, 4].map((i) => (
                    <div key={i} className="flex justify-between items-center py-2">
                        <div className="h-4 w-24 bg-muted/10 rounded skeleton" />
                        <div className="h-4 w-20 bg-muted/10 rounded skeleton" />
                    </div>
                ))}

                {/* Total */}
                <div className="pt-3 border-t border-muted/10">
                    <div className="flex justify-between items-center">
                        <div className="h-5 w-12 bg-muted/10 rounded skeleton" />
                        <div className="h-7 w-24 bg-muted/10 rounded skeleton" />
                    </div>
                </div>
            </div>
        </div>
    );
};

// ============================================================================
// Items List Skeleton
// ============================================================================

export const OrderItemsListSkeleton: React.FC = () => {
    return (
        <div className="bg-background border border-muted/10 rounded-xl overflow-hidden animate-pulse">
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <div className="h-6 w-28 bg-muted/10 rounded skeleton" />
            </div>

            {/* Items */}
            <div className="px-5">
                {[1, 2, 3].map((i) => (
                    <div
                        key={i}
                        className="flex gap-4 py-4 border-b border-muted/10 last:border-b-0"
                    >
                        <div className="w-16 h-16 bg-muted/10 rounded-lg skeleton flex-shrink-0" />
                        <div className="flex-1 space-y-2">
                            <div className="h-5 w-3/4 bg-muted/10 rounded skeleton" />
                            <div className="h-3 w-20 bg-muted/10 rounded skeleton" />
                            <div className="flex justify-between">
                                <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                                <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                            </div>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};

// ============================================================================
// Shipping Address Skeleton
// ============================================================================

export const ShippingAddressCardSkeleton: React.FC = () => {
    return (
        <div className="bg-background border border-muted/10 rounded-xl overflow-hidden animate-pulse">
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <div className="h-6 w-36 bg-muted/10 rounded skeleton" />
            </div>

            {/* Content */}
            <div className="p-5 space-y-4">
                {[1, 2, 3].map((i) => (
                    <div key={i} className="flex gap-3">
                        <div className="w-8 h-8 rounded-full bg-muted/10 skeleton flex-shrink-0" />
                        <div className="flex-1 space-y-2">
                            <div className="h-3 w-16 bg-muted/10 rounded skeleton" />
                            <div className="h-5 w-full bg-muted/10 rounded skeleton" />
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};

// ============================================================================
// Full Page Skeleton
// ============================================================================

export const OrderPageSkeleton: React.FC = () => {
    return (
        <div className="min-h-screen bg-background">
            <div className="container mx-auto px-4 py-8 max-w-4xl">
                {/* Header Skeleton */}
                <div className="text-center mb-8">
                    <div className="h-8 w-48 bg-muted/10 rounded skeleton mx-auto mb-2" />
                    <div className="h-5 w-64 bg-muted/10 rounded skeleton mx-auto" />
                </div>

                {/* Timeline Skeleton */}
                <div className="mb-8 p-6 bg-muted/5 rounded-xl">
                    <OrderTimelineSkeleton />
                </div>

                {/* Content Grid */}
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                    <OrderSummaryCardSkeleton />
                    <ShippingAddressCardSkeleton />
                </div>

                {/* Items Skeleton */}
                <div className="mt-6">
                    <OrderItemsListSkeleton />
                </div>
            </div>
        </div>
    );
};
