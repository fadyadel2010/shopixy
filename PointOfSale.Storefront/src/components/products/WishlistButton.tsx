/**
 * WishlistButton Component
 * Phase 10.3.6 - Wishlist (Production-grade)
 * 
 * Heart icon button for adding/removing products from wishlist.
 * Used on product cards and product detail pages.
 * 
 * Features:
 * - Optimistic UI updates
 * - Loading state
 * - Auth-aware (redirects to login if not authenticated)
 * - RTL safe
 * - Accessible
 */

import React, { useState, useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import { useWishlist, useIsInWishlist } from '@/hooks/useWishlist';
import { useAuthStore, selectIsAuthenticated, selectAuthStatus } from '@/auth/store/authStore';
import { useTranslation } from '@/i18n';
import './WishlistButton.css';

interface WishlistButtonProps {
    productId: string;
    size?: 'small' | 'medium' | 'large';
    variant?: 'icon' | 'button';
    className?: string;
    onError?: (error: Error) => void;
}

export function WishlistButton({
    productId,
    size = 'medium',
    variant = 'icon',
    className = '',
    onError,
}: WishlistButtonProps) {
    const { t } = useTranslation();
    const navigate = useAffiliateNavigate();
    const location = useLocation();

    const isAuthenticated = useAuthStore(selectIsAuthenticated);
    const authStatus = useAuthStore(selectAuthStatus);
    const isInWishlist = useIsInWishlist(productId);
    const { toggleItem } = useWishlist();

    const [isLoading, setIsLoading] = useState(false);
    const [isAnimating, setIsAnimating] = useState(false);

    const handleClick = useCallback(async (e: React.MouseEvent) => {
        e.preventDefault();
        e.stopPropagation();

        if (authStatus === 'degraded') {
            const error = new Error(t('wishlist.reconnecting'));
            onError?.(error);
            return;
        }

        // Redirect to login if not authenticated
        if (!isAuthenticated) {
            const returnUrl = encodeURIComponent(location.pathname + location.search);
            navigate(`/login?return=${returnUrl}`);
            return;
        }

        setIsLoading(true);

        try {
            // Trigger animation
            setIsAnimating(true);
            setTimeout(() => setIsAnimating(false), 300);

            await toggleItem(productId);
        } catch (error) {
            console.error('[WishlistButton] Toggle failed:', error);
            if (onError && error instanceof Error) {
                onError(error);
            }
        } finally {
            setIsLoading(false);
        }
    }, [authStatus, isAuthenticated, productId, toggleItem, navigate, location, onError, t]);

    const sizeClasses = {
        small: 'wishlist-btn--small',
        medium: 'wishlist-btn--medium',
        large: 'wishlist-btn--large',
    };

    const buttonClasses = [
        'wishlist-btn',
        sizeClasses[size],
        variant === 'button' ? 'wishlist-btn--button' : 'wishlist-btn--icon',
        isInWishlist ? 'wishlist-btn--active' : '',
        isLoading ? 'wishlist-btn--loading' : '',
        isAnimating ? 'wishlist-btn--animating' : '',
        className,
    ].filter(Boolean).join(' ');

    const ariaLabel = isInWishlist
        ? t('wishlist.removeFromWishlist')
        : t('wishlist.addToWishlist');
    const isDisabledByAuthState = authStatus === 'degraded';

    return (
        <button
            type="button"
            className={buttonClasses}
            onClick={handleClick}
            disabled={isLoading || isDisabledByAuthState}
            aria-label={ariaLabel}
            title={isDisabledByAuthState
                ? t('wishlist.reconnectingShort')
                : ariaLabel}
        >
            <svg
                className="wishlist-btn__icon"
                viewBox="0 0 24 24"
                fill={isInWishlist ? 'currentColor' : 'none'}
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
            >
                <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z" />
            </svg>
            {variant === 'button' && (
                <span className="wishlist-btn__text">
                    {isInWishlist ? t('wishlist.remove') : t('wishlist.add')}
                </span>
            )}
            {isLoading && (
                <span className="wishlist-btn__spinner" aria-hidden="true" />
            )}
        </button>
    );
}

export default WishlistButton;
