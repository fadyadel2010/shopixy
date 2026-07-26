/**
 * Cart Badge Component
 * 
 * Animated badge showing cart item count.
 * Features:
 * - Pulse animation on count change
 * - Smooth scale transition
 * - Hides when count = 0
 * - Respects prefers-reduced-motion
 * - Keyboard accessible
 */

import React, { useEffect, useRef, useState } from 'react';
import { useCartStore, selectCartItemCount } from '@/store';

interface CartBadgeProps {
    /** Override count (for testing) */
    count?: number;
    /** Additional classes */
    className?: string;
}

/**
 * Cart Badge with animation on count change
 */
export const CartBadge: React.FC<CartBadgeProps> = ({ count: overrideCount, className = '' }) => {
    const storeCount = useCartStore(selectCartItemCount);
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
                bg-primary text-white
                text-xs font-bold
                rounded-full
                transition-transform duration-200
                ${isAnimating ? 'animate-badge-pulse' : ''}
                motion-reduce:animate-none
                ${className}
            `}
            aria-label={`${count} item${count !== 1 ? 's' : ''} in cart`}
        >
            {displayCount}
        </span>
    );
};

/**
 * Cart Icon with Badge
 * Complete cart button with icon and animated badge
 */
interface CartIconButtonProps {
    /** Button style classes */
    className?: string;
    /** Whether to show tooltip */
    showTooltip?: boolean;
}

export const CartIconButton: React.FC<CartIconButtonProps> = ({
    className = '',
    showTooltip = false,
}) => {
    const count = useCartStore(selectCartItemCount);
    const [showTip, setShowTip] = useState(false);

    return (
        <a
            href="/cart"
            className={`relative p-2 rounded-lg transition-colors group ${className}`}
            aria-label={`Shopping cart${count > 0 ? `, ${count} item${count !== 1 ? 's' : ''}` : ''}`}
            onMouseEnter={() => showTooltip && setShowTip(true)}
            onMouseLeave={() => setShowTip(false)}
            onFocus={() => showTooltip && setShowTip(true)}
            onBlur={() => setShowTip(false)}
        >
            {/* Cart Icon */}
            <svg
                className="w-5 h-5 transition-transform group-hover:scale-110"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                aria-hidden="true"
            >
                <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z"
                />
            </svg>

            {/* Badge */}
            <CartBadge />

            {/* Tooltip */}
            {showTooltip && showTip && count > 0 && (
                <span
                    className="absolute -bottom-8 left-1/2 -translate-x-1/2 px-2 py-1 bg-secondary text-white text-xs rounded whitespace-nowrap animate-fade-in"
                    role="tooltip"
                >
                    {count} item{count !== 1 ? 's' : ''} in cart
                </span>
            )}
        </a>
    );
};
