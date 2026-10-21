/**
 * AF-13B: Affiliate Guest Checkout Banner
 * 
 * Displayed on the checkout page when the storefront is in Affiliate Mode.
 * Communicates that no login is required — reducing friction for affiliate traffic.
 * 
 * This component is purely cosmetic — the backend already accepts guest checkout
 * via session-based cart ownership (X-Session-Id header). The banner provides
 * UX transparency so users don't look for a login button.
 */

import React, { useSyncExternalStore } from 'react';
import { subscribeAffiliateRef, getConfirmedAffiliateRefCode } from '@/bootstrap/affiliateRefTracker';

interface AffiliateGuestCheckoutBannerProps {
    /** Optional className override */
    className?: string;
}

export const AffiliateGuestCheckoutBanner: React.FC<AffiliateGuestCheckoutBannerProps> = ({
    className = '',
}) => {
    // Only show in affiliate mode
    const refCode = useSyncExternalStore(subscribeAffiliateRef, getConfirmedAffiliateRefCode);
    if (!refCode) return null;

    return (
        <div
            id="affiliate-guest-checkout-banner"
            className={className}
            style={{
                background: 'linear-gradient(135deg, #0ea5e9 0%, #8b5cf6 100%)',
                borderRadius: '12px',
                padding: '16px 20px',
                marginBottom: '20px',
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                color: '#ffffff',
                boxShadow: '0 4px 14px rgba(14, 165, 233, 0.25)',
            }}
        >
            {/* Icon */}
            <div style={{
                width: '40px',
                height: '40px',
                borderRadius: '50%',
                background: 'rgba(255,255,255,0.2)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
                fontSize: '1.2rem',
            }}>
                ⚡
            </div>

            {/* Text */}
            <div>
                <div style={{
                    fontWeight: 600,
                    fontSize: '0.95rem',
                    marginBottom: '2px',
                }}>
                    Fast checkout — no account required
                </div>
                <div style={{
                    fontSize: '0.8rem',
                    opacity: 0.85,
                }}>
                    Just fill in your details and place your order
                </div>
            </div>
        </div>
    );
};
