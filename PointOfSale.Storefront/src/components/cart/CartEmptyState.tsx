/**
 * Cart Empty State Component
 * 
 * Friendly empty state for empty cart.
 */

import React from 'react';
import { Button } from '@/components/common';
import { useTranslation } from '@/i18n';

interface CartEmptyStateProps {
    /** Continue shopping URL */
    continueShoppingUrl?: string;
}

/**
 * Cart Empty State Component
 */
export const CartEmptyState: React.FC<CartEmptyStateProps> = ({
    continueShoppingUrl = '/products',
}) => {
    const { t, language } = useTranslation();
    return (
        <div className="flex flex-col items-center justify-center py-16 text-center" dir={language === 'ar' ? 'rtl' : 'ltr'}>
            {/* Icon */}
            <div className="w-24 h-24 mb-6 flex items-center justify-center bg-muted/10 rounded-full">
                <svg
                    className="w-12 h-12 text-muted"
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

            {/* Title */}
            <h2 className="text-2xl font-bold text-text mb-2">
                {t('cart.empty')}
            </h2>

            {/* Message */}
            <p className="text-muted mb-8 max-w-sm">
                {t('cart.emptyDescription')}
            </p>

            {/* CTA */}
            <a href={continueShoppingUrl}>
                <Button size="lg" variant="primary">
                    {t('cart.continueShopping')}
                    <svg className={`w-5 h-5 ${language === 'ar' ? 'mr-2 rotate-180' : 'ml-2'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 8l4 4m0 0l-4 4m4-4H3" />
                    </svg>
                </Button>
            </a>
        </div>
    );
};
