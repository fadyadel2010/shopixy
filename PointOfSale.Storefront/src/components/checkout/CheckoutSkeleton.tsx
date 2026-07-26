/**
 * Checkout Skeleton Component
 * 
 * Loading skeleton for checkout page.
 * Matches the layout of the checkout form.
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React from 'react';

/**
 * Checkout Skeleton Component
 */
export const CheckoutSkeleton: React.FC = () => {
    return (
        <div className="animate-pulse">
            {/* Stepper skeleton */}
            <div className="flex items-center justify-center gap-4 mb-8">
                {[1, 2, 3].map((i) => (
                    <React.Fragment key={i}>
                        <div className="w-24 h-10 bg-muted/10 rounded-lg skeleton" />
                        {i < 3 && <div className="w-12 h-0.5 bg-muted/10" />}
                    </React.Fragment>
                ))}
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
                {/* Form skeleton */}
                <div className="lg:col-span-2 space-y-6">
                    {/* Section header */}
                    <div className="h-6 w-40 bg-muted/10 rounded skeleton" />

                    {/* Form fields */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        {[1, 2, 3, 4].map((i) => (
                            <div key={i} className="space-y-2">
                                <div className="h-4 w-24 bg-muted/10 rounded skeleton" />
                                <div className="h-12 bg-muted/10 rounded-lg skeleton" />
                            </div>
                        ))}
                        <div className="md:col-span-2 space-y-2">
                            <div className="h-4 w-32 bg-muted/10 rounded skeleton" />
                            <div className="h-24 bg-muted/10 rounded-lg skeleton" />
                        </div>
                    </div>

                    {/* Shipping methods section */}
                    <div className="h-6 w-48 bg-muted/10 rounded skeleton mt-8" />
                    <div className="space-y-3">
                        {[1, 2].map((i) => (
                            <div key={i} className="h-20 bg-muted/10 rounded-lg skeleton" />
                        ))}
                    </div>

                    {/* Button */}
                    <div className="h-14 bg-muted/10 rounded-xl skeleton mt-6" />
                </div>

                {/* Order summary skeleton */}
                <div className="lg:col-span-1">
                    <div className="bg-muted/5 rounded-xl overflow-hidden">
                        <div className="p-4 border-b border-muted/10">
                            <div className="h-5 w-32 bg-muted/10 rounded skeleton" />
                        </div>
                        <div className="p-4 space-y-3">
                            {[1, 2, 3].map((i) => (
                                <div key={i} className="flex gap-3">
                                    <div className="w-12 h-12 bg-muted/10 rounded-lg skeleton" />
                                    <div className="flex-1 space-y-2">
                                        <div className="h-4 bg-muted/10 rounded skeleton" />
                                        <div className="h-3 w-16 bg-muted/10 rounded skeleton" />
                                    </div>
                                    <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                                </div>
                            ))}
                        </div>
                        <div className="p-4 border-t border-muted/10 space-y-3">
                            <div className="flex justify-between">
                                <div className="h-4 w-20 bg-muted/10 rounded skeleton" />
                                <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                            </div>
                            <div className="flex justify-between">
                                <div className="h-4 w-16 bg-muted/10 rounded skeleton" />
                                <div className="h-4 w-12 bg-muted/10 rounded skeleton" />
                            </div>
                            <hr className="border-muted/10" />
                            <div className="flex justify-between">
                                <div className="h-5 w-12 bg-muted/10 rounded skeleton" />
                                <div className="h-6 w-20 bg-muted/10 rounded skeleton" />
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};
