/**
 * Category Promotion Banner
 * 
 * PHASE: Promotions Engine - Category Banner Support
 * 
 * Displays a prominent promotion banner at the top of category pages
 * when a category-level promotion is active. Includes countdown timer
 * if the promotion has an end date.
 * 
 * Features:
 * - 4 selectable design variants (gradient, solid, minimal, festive)
 * - Dynamic theme colors using CSS variables
 * - Full i18n support (English + Arabic)
 * - RTL layout support
 * - Countdown timer
 * 
 * Architecture:
 * - Data comes from MongoDB CategoryDocument (read-only)
 * - Countdown uses server-provided EndDate (no client-side drift)
 * - Banner disappears automatically when promotion expires
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { usePromotionCountdown } from '@/hooks/usePromotionCountdown';

// Banner style variants that can be selected in admin
export type BannerStyle = 'gradient' | 'solid' | 'minimal' | 'festive';

interface CategoryPromotionBannerProps {
    /** Whether the category has an active promotion */
    hasActivePromotion?: boolean;
    /** Banner text to display */
    bannerText?: string;
    /** End date of promotion for countdown (ISO string) */
    endDate?: string;
    /** Category name for accessibility and subtitle */
    categoryName?: string;
    /** Banner design style (default: gradient) */
    bannerStyle?: BannerStyle;
}

/**
 * Category Promotion Banner Component
 */
export const CategoryPromotionBanner: React.FC<CategoryPromotionBannerProps> = ({
    hasActivePromotion,
    bannerText,
    endDate,
    categoryName,
    bannerStyle = 'gradient',
}) => {
    const { t, isRTL } = useTranslation();
    const countdown = usePromotionCountdown(endDate);

    // Don't render if no active promotion or if expired
    if (!hasActivePromotion || !bannerText || countdown.isExpired) {
        return null;
    }

    // Get style classes based on banner variant
    const getStyleClasses = () => {
        switch (bannerStyle) {
            case 'gradient':
                return 'bg-gradient-to-r from-primary via-accent to-primary';
            case 'solid':
                return 'bg-primary';
            case 'minimal':
                return 'bg-surface border-2 border-primary';
            case 'festive':
                return 'bg-gradient-to-r from-amber-500 via-red-500 to-pink-500';
            default:
                return 'bg-gradient-to-r from-primary via-accent to-primary';
        }
    };

    // Get text color based on variant
    const getTextColor = () => {
        switch (bannerStyle) {
            case 'minimal':
                return 'text-primary';
            default:
                return 'text-white';
        }
    };

    // Get secondary text color
    const getSecondaryTextColor = () => {
        switch (bannerStyle) {
            case 'minimal':
                return 'text-muted';
            default:
                return 'text-white/80';
        }
    };

    // Get icon background
    const getIconBackground = () => {
        switch (bannerStyle) {
            case 'minimal':
                return 'bg-primary/10';
            default:
                return 'bg-white/20';
        }
    };

    // Get countdown background
    const getCountdownBackground = () => {
        switch (bannerStyle) {
            case 'minimal':
                return 'bg-primary/10';
            default:
                return 'bg-white/10 backdrop-blur-sm';
        }
    };

    return (
        <div
            className={`
                relative overflow-hidden rounded-xl p-4 md:p-6 mb-6 shadow-lg
                ${getStyleClasses()}
            `}
            role="banner"
            aria-label={`${t('promotion.limitedTime')}: ${categoryName || ''}`}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            {/* Animated background elements for festive style */}
            {bannerStyle === 'festive' && (
                <div className="absolute inset-0 opacity-30">
                    <div className="absolute top-1/4 left-1/4 w-2 h-2 bg-white rounded-full animate-pulse" />
                    <div className="absolute top-3/4 left-1/2 w-1 h-1 bg-white rounded-full animate-ping" />
                    <div className="absolute top-1/2 right-1/4 w-2 h-2 bg-white rounded-full animate-pulse" />
                    <div className="absolute top-1/3 right-1/3 w-1.5 h-1.5 bg-yellow-300 rounded-full animate-bounce" />
                </div>
            )}

            {/* Gradient style sparkles */}
            {bannerStyle === 'gradient' && (
                <div className="absolute inset-0 opacity-20">
                    <div className="absolute top-1/4 left-1/4 w-2 h-2 bg-white rounded-full animate-pulse" />
                    <div className="absolute top-3/4 left-1/2 w-1 h-1 bg-white rounded-full animate-ping" />
                    <div className="absolute top-1/2 right-1/4 w-2 h-2 bg-white rounded-full animate-pulse delay-500" />
                </div>
            )}

            <div className={`
                relative z-10 flex flex-col md:flex-row items-center justify-between gap-4
                ${isRTL ? 'md:flex-row-reverse' : ''}
            `}>
                {/* Promotion Text */}
                <div className={`flex items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    {/* Sale Icon */}
                    <div className={`flex-shrink-0 w-12 h-12 rounded-full flex items-center justify-center ${getIconBackground()}`}>
                        <svg
                            className={`w-6 h-6 ${getTextColor()}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                        >
                            <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={2}
                                d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
                            />
                        </svg>
                    </div>

                    {/* Banner Text */}
                    <div className={isRTL ? 'text-right' : 'text-left'}>
                        <h2 className={`text-xl md:text-2xl font-bold ${getTextColor()}`}>
                            {bannerText}
                        </h2>
                        {categoryName && (
                            <p className={`text-sm ${getSecondaryTextColor()}`}>
                                {t('promotion.onAllProducts', { category: categoryName })}
                            </p>
                        )}
                    </div>
                </div>

                {/* Countdown Timer (if end date exists) */}
                {endDate && !countdown.isExpired && (
                    <div className={`
                        flex items-center gap-2 rounded-lg px-4 py-2
                        ${getCountdownBackground()}
                        ${isRTL ? 'flex-row-reverse' : ''}
                    `}>
                        <svg
                            className={`w-5 h-5 ${getTextColor()}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                        >
                            <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={2}
                                d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
                            />
                        </svg>
                        <div className={getTextColor()}>
                            <span className={`text-xs ${getSecondaryTextColor()}`}>
                                {t('promotion.endsIn')}
                            </span>
                            <p className="font-bold text-lg">{countdown.timeRemaining}</p>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default CategoryPromotionBanner;
