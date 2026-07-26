/**
 * Wishlist Badge Component
 * Phase 10.3.6 - Wishlist Enhancements
 * 
 * Animated badge showing wishlist item count.
 * Features:
 * - Pulse animation on count change
 * - Smooth scale transition
 * - Hides when count = 0
 * - Respects prefers-reduced-motion
 * - Keyboard accessible
 */

import React, { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useWishlistCount } from '@/hooks/useWishlist';
import { useTranslation } from '@/i18n';

interface WishlistBadgeProps {
    /** Override count (for testing) */
    count?: number;
    /** Additional classes */
    className?: string;
}

/**
 * Wishlist Badge with animation on count change
 */
export const WishlistBadge: React.FC<WishlistBadgeProps> = ({ count: overrideCount, className = '' }) => {
    const storeCount = useWishlistCount();
    const count = overrideCount ?? storeCount;
    const prevCountRef = useRef(count);
    const [isAnimating, setIsAnimating] = useState(false);

    // Trigger animation on count change
    useEffect(() => {
        if (count !== prevCountRef.current && count > 0) {
            setIsAnimating(true);
            const timeout = setTimeout(() => setIsAnimating(false), 300);
            prevCountRef.current = count;
            return () => clearTimeout(timeout);
        }
        prevCountRef.current = count;
    }, [count]);

    // Don't render if count is 0
    if (count <= 0) return null;

    // Format count (99+ for large numbers)
    const displayCount = count > 99 ? '99+' : count.toString();

    return (
        <span
            className={`
                absolute -top-1 -right-1
                min-w-[1.25rem] h-5
                flex items-center justify-center
                px-1.5
                bg-red-500 text-white
                text-xs font-bold
                rounded-full
                transition-transform duration-200
                ${isAnimating ? 'animate-badge-pulse' : ''}
                motion-reduce:animate-none
                ${className}
            `}
            aria-label={`${count} item${count !== 1 ? 's' : ''} in wishlist`}
        >
            {displayCount}
        </span>
    );
};

/**
 * Wishlist Icon with Badge
 * Complete wishlist button with icon and animated badge for header use
 */
interface WishlistIconButtonProps {
    /** Button style classes */
    className?: string;
    /** Whether to show tooltip */
    showTooltip?: boolean;
}

export const WishlistIconButton: React.FC<WishlistIconButtonProps> = ({
    className = '',
    showTooltip = false,
}) => {
    const { t } = useTranslation();
    const count = useWishlistCount();
    const [showTip, setShowTip] = useState(false);

    return (
        <Link
            to="/wishlist"
            className={`relative p-2 rounded-lg transition-colors group ${className}`}
            aria-label={`${t('wishlist.title')}${count > 0 ? `, ${count} item${count !== 1 ? 's' : ''}` : ''}`}
            onMouseEnter={() => showTooltip && setShowTip(true)}
            onMouseLeave={() => setShowTip(false)}
            onFocus={() => showTooltip && setShowTip(true)}
            onBlur={() => setShowTip(false)}
        >
            {/* Heart Icon */}
            <svg
                className="w-5 h-5 transition-transform group-hover:scale-110"
                fill={count > 0 ? 'currentColor' : 'none'}
                stroke="currentColor"
                viewBox="0 0 24 24"
                aria-hidden="true"
            >
                <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z"
                />
            </svg>

            {/* Badge */}
            <WishlistBadge />

            {/* Tooltip */}
            {showTooltip && showTip && (
                <span
                    className="absolute -bottom-8 left-1/2 -translate-x-1/2 px-2 py-1 bg-secondary text-white text-xs rounded whitespace-nowrap animate-fade-in z-10"
                    role="tooltip"
                >
                    {count > 0
                        ? `${count} ${t('wishlist.count', { count }).replace('{count}', count.toString())}`
                        : t('wishlist.title')
                    }
                </span>
            )}
        </Link>
    );
};
