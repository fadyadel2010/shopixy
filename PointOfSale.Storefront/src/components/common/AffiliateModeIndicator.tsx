/**
 * AF-12: Affiliate Mode Indicator
 * 
 * Displays a visual badge when the storefront is in Affiliate Mode.
 * Affiliate Mode is activated when a visitor arrives with ?ref=CODE.
 * 
 * Design:
 * - Small, non-intrusive badge in header
 * - Shows "🎯 Affiliate Store" text
 * - Tooltip explains the curated collection
 * - Only visible when ?ref= query parameter is present
 * 
 * UPDATED: Now uses AffiliateContext for centralized state management
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { useIsAffiliateMode } from '@/contexts/AffiliateContext';

interface AffiliateModeIndicatorProps {
    className?: string;
}

export const AffiliateModeIndicator: React.FC<AffiliateModeIndicatorProps> = ({ className = '' }) => {
    const { t } = useTranslation();
    const isAffiliateMode = useIsAffiliateMode();

    // Don't render if not in Affiliate Mode
    if (!isAffiliateMode) {
        return null;
    }

    return (
        <div
            className={`
                inline-flex items-center gap-1.5 px-3 py-1.5 
                bg-amber-500/20 text-amber-200 
                rounded-full text-xs font-medium
                border border-amber-500/30
                backdrop-blur-sm
                ${className}
            `}
            role="status"
            aria-label={t('affiliate.modeActive') || 'Affiliate Store - Curated Collection'}
            title={t('affiliate.modeTooltip') || 'You are browsing a curated collection of affiliate products'}
        >
            <span className="text-sm" aria-hidden="true">🎯</span>
            <span className="hidden sm:inline">
                {t('affiliate.storeBadge') || 'Affiliate Store'}
            </span>
        </div>
    );
};

/**
 * Hook to detect Affiliate Mode
 * Returns true if ?ref= query parameter is present
 * 
 * @deprecated Use useIsAffiliateMode from AffiliateContext instead
 */
export function useAffiliateMode(): boolean {
    return useIsAffiliateMode();
}
