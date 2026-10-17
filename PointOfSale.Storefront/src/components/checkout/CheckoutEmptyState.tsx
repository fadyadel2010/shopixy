/**
 * Checkout Empty State Component
 * 
 * Shown when user tries to checkout with empty cart.
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React from 'react';
import { useTranslation } from '@/i18n';

/**
 * Checkout Empty State Component
 */
export const CheckoutEmptyState: React.FC = () => {
    const { t, language } = useTranslation();
    return (
        <div className="text-center py-16 px-4" dir={language === 'ar' ? 'rtl' : 'ltr'}>
            {/* Icon */}
            <div className="mb-6">
                <div className="w-20 h-20 mx-auto rounded-full bg-muted/10 flex items-center justify-center">
                    <svg
                        className="w-10 h-10 text-muted"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={1.5}
                            d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z"
                        />
                    </svg>
                </div>
            </div>

            {/* Heading */}
            <h1 className="text-2xl font-bold text-text mb-2">
                {t('cart.empty')}
            </h1>

            <p className="text-muted mb-8 max-w-sm mx-auto">
                {t('cart.emptyDescription')}
            </p>

            {/* Action Button */}
            <a
                href="/products"
                className="
                    inline-flex items-center gap-2 py-3 px-6
                    rounded-xl font-medium
                    bg-primary text-white
                    hover:bg-primary/90
                    transition-colors
                    focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                "
            >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
                </svg>
                {t('cart.continueShopping')}
            </a>
        </div>
    );
};
