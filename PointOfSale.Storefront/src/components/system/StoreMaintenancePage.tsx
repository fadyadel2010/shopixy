/**
 * Store Maintenance Page Component
 * 
 * Shown when storefront is in maintenance mode.
 * Uses store branding (colors, fonts) for consistency.
 * System-level screen - no header/footer.
 * 
 * PHASE 7.10: POS ↔ Storefront Bridge Polish
 */

import React from 'react';
import { useStoreConfigStore, selectStoreName, selectTheme } from '@/store';

interface StoreMaintenancePageProps {
    /** Custom maintenance message from store config */
    message?: string;
}

export const StoreMaintenancePage: React.FC<StoreMaintenancePageProps> = ({
    message
}) => {
    const storeName = useStoreConfigStore(selectStoreName);
    const theme = useStoreConfigStore(selectTheme);

    const primaryColor = theme?.colors?.primary || '#2563eb';
    const textColor = theme?.colors?.text || '#020617';
    const mutedColor = theme?.colors?.muted || '#64748b';

    const defaultMessage = "We're making some improvements to give you a better shopping experience.";
    const displayMessage = message || defaultMessage;

    return (
        <div
            className="min-h-screen flex items-center justify-center p-4"
            style={{ backgroundColor: theme?.colors?.background || '#ffffff' }}
            role="main"
            aria-labelledby="maintenance-title"
        >
            <div className="max-w-lg w-full text-center p-8">
                {/* Animated Icon */}
                <div
                    className="w-24 h-24 mx-auto mb-8 rounded-full flex items-center justify-center"
                    style={{
                        backgroundColor: `${primaryColor}15`,
                        animation: 'pulse 2s ease-in-out infinite'
                    }}
                >
                    <svg
                        className="w-12 h-12"
                        style={{ color: primaryColor }}
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                        aria-hidden="true"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={1.5}
                            d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"
                        />
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={1.5}
                            d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
                        />
                    </svg>
                </div>

                {/* Store Name */}
                <p
                    className="text-sm font-medium tracking-wide uppercase mb-4"
                    style={{ color: mutedColor }}
                >
                    {storeName || 'Store'}
                </p>

                {/* Title */}
                <h1
                    id="maintenance-title"
                    className="text-3xl font-bold mb-4"
                    style={{ color: textColor }}
                    tabIndex={-1}
                >
                    We'll Be Right Back
                </h1>

                {/* Message */}
                <p
                    className="text-lg mb-8"
                    style={{ color: mutedColor }}
                >
                    {displayMessage}
                </p>

                {/* Progress indicator */}
                <div
                    className="w-48 h-1 mx-auto rounded-full overflow-hidden mb-8"
                    style={{ backgroundColor: `${primaryColor}20` }}
                >
                    <div
                        className="h-full rounded-full"
                        style={{
                            backgroundColor: primaryColor,
                            width: '60%',
                            animation: 'loading 2s ease-in-out infinite'
                        }}
                    />
                </div>

                {/* Info text */}
                <p
                    className="text-sm"
                    style={{ color: mutedColor }}
                >
                    Please check back soon. We appreciate your patience.
                </p>
            </div>

            {/* Keyframe animations */}
            <style>{`
                @keyframes pulse {
                    0%, 100% { transform: scale(1); opacity: 1; }
                    50% { transform: scale(1.05); opacity: 0.8; }
                }
                @keyframes loading {
                    0% { transform: translateX(-100%); }
                    50% { transform: translateX(100%); }
                    100% { transform: translateX(-100%); }
                }
            `}</style>
        </div>
    );
};
