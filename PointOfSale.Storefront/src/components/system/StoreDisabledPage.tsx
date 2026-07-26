/**
 * Store Disabled Page Component
 * 
 * Shown when storefront is disabled by the merchant.
 * Uses store branding (colors, fonts) for consistency.
 * System-level screen - no header/footer.
 * 
 * PHASE 7.10: POS ↔ Storefront Bridge Polish
 */

import React from 'react';
import { useStoreConfigStore, selectStoreName, selectTheme } from '@/store';

interface StoreDisabledPageProps {
    /** Optional custom message */
    message?: string;
}

export const StoreDisabledPage: React.FC<StoreDisabledPageProps> = ({
    message
}) => {
    const storeName = useStoreConfigStore(selectStoreName);
    const theme = useStoreConfigStore(selectTheme);

    const primaryColor = theme?.colors?.primary || '#2563eb';
    const textColor = theme?.colors?.text || '#020617';
    const mutedColor = theme?.colors?.muted || '#64748b';
    const backgroundColor = theme?.colors?.background || '#ffffff';

    const displayMessage = message || "This online store is currently not accepting orders.";

    return (
        <div
            className="min-h-screen flex items-center justify-center p-4"
            style={{ backgroundColor }}
            role="main"
            aria-labelledby="disabled-title"
        >
            <div className="max-w-md w-full text-center p-8">
                {/* Icon */}
                <div
                    className="w-20 h-20 mx-auto mb-6 rounded-full flex items-center justify-center"
                    style={{ backgroundColor: '#fef2f2' }}
                >
                    <svg
                        className="w-10 h-10 text-red-500"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                        aria-hidden="true"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636"
                        />
                    </svg>
                </div>

                {/* Store Name */}
                <p
                    className="text-sm font-medium tracking-wide uppercase mb-3"
                    style={{ color: mutedColor }}
                >
                    {storeName || 'Store'}
                </p>

                {/* Title */}
                <h1
                    id="disabled-title"
                    className="text-2xl font-bold mb-4"
                    style={{ color: textColor }}
                    tabIndex={-1}
                >
                    Store Unavailable
                </h1>

                {/* Message */}
                <p
                    className="text-base mb-8"
                    style={{ color: mutedColor }}
                >
                    {displayMessage}
                </p>

                {/* Divider */}
                <div
                    className="w-16 h-px mx-auto mb-8"
                    style={{ backgroundColor: `${textColor}20` }}
                />

                {/* Additional info */}
                <div
                    className="text-sm p-4 rounded-lg"
                    style={{
                        backgroundColor: `${primaryColor}08`,
                        color: mutedColor
                    }}
                >
                    <p className="mb-2">
                        <strong style={{ color: textColor }}>Looking to shop?</strong>
                    </p>
                    <p>
                        The store owner has temporarily disabled online ordering.
                        Please try again later or contact the store directly.
                    </p>
                </div>

                {/* Contact hint */}
                <p
                    className="mt-8 text-xs"
                    style={{ color: mutedColor }}
                >
                    If you believe this is an error, please contact the store owner.
                </p>
            </div>
        </div>
    );
};
