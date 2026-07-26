/**
 * Shipping Address Card Component
 * 
 * Displays shipping address information in a readable format.
 * Read-only, accessible layout.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';
import type { OrderShippingAddress } from '@/types/order';

// ============================================================================
// Types
// ============================================================================

interface ShippingAddressCardProps {
    address: OrderShippingAddress;
    className?: string;
}

// ============================================================================
// Main Component
// ============================================================================

export const ShippingAddressCard: React.FC<ShippingAddressCardProps> = ({
    address,
    className = '',
}) => {
    return (
        <div
            className={`
                bg-background border border-muted/10 rounded-xl overflow-hidden
                ${className}
            `}
        >
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <h2 className="text-lg font-semibold text-text flex items-center gap-2">
                    <svg
                        className="w-5 h-5 text-primary"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z"
                        />
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M15 11a3 3 0 11-6 0 3 3 0 016 0z"
                        />
                    </svg>
                    Shipping Address
                </h2>
            </div>

            {/* Content */}
            <div className="p-5">
                <address className="not-italic space-y-3">
                    {/* Name */}
                    <div className="flex items-start gap-3">
                        <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
                            <svg
                                className="w-4 h-4 text-primary"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"
                                />
                            </svg>
                        </div>
                        <div>
                            <p className="text-sm text-muted">Recipient</p>
                            <p className="font-semibold text-text">{address.fullName}</p>
                        </div>
                    </div>

                    {/* Phone */}
                    <div className="flex items-start gap-3">
                        <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
                            <svg
                                className="w-4 h-4 text-primary"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z"
                                />
                            </svg>
                        </div>
                        <div>
                            <p className="text-sm text-muted">Phone</p>
                            <p className="font-medium text-text">
                                <a
                                    href={`tel:${address.phone}`}
                                    className="hover:text-primary transition-colors"
                                >
                                    {address.phone}
                                </a>
                            </p>
                        </div>
                    </div>

                    {/* Address */}
                    <div className="flex items-start gap-3">
                        <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
                            <svg
                                className="w-4 h-4 text-primary"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6"
                                />
                            </svg>
                        </div>
                        <div>
                            <p className="text-sm text-muted">Address</p>
                            <p className="font-medium text-text">{address.addressLine}</p>
                            <p className="text-muted text-sm">
                                {address.area}, {address.city}
                            </p>
                        </div>
                    </div>

                    {/* Notes */}
                    {address.notes && (
                        <div className="flex items-start gap-3 pt-2 border-t border-muted/10">
                            <div className="w-8 h-8 rounded-full bg-amber-100 flex items-center justify-center flex-shrink-0">
                                <svg
                                    className="w-4 h-4 text-amber-600"
                                    fill="none"
                                    stroke="currentColor"
                                    viewBox="0 0 24 24"
                                >
                                    <path
                                        strokeLinecap="round"
                                        strokeLinejoin="round"
                                        strokeWidth={2}
                                        d="M7 8h10M7 12h4m1 8l-4-4H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-3l-4 4z"
                                    />
                                </svg>
                            </div>
                            <div>
                                <p className="text-sm text-muted">Delivery Notes</p>
                                <p className="text-text italic">{address.notes}</p>
                            </div>
                        </div>
                    )}
                </address>
            </div>
        </div>
    );
};
