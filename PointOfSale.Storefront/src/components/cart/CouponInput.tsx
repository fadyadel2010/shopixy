/**
 * CouponInput Component
 * 
 * Stateless component for entering and applying coupon codes.
 * All business logic is delegated to cartStore - this component only handles UI.
 * 
 * Phase C4: Frontend Coupon UI
 * Phase 10.4.Y: Currency hardening - uses useCurrency() hook, no hardcoded defaults.
 */

import { useState, useCallback, useEffect, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from '@/i18n';
import { useCurrency } from '@/hooks';
import {
    useCartStore,
    selectAppliedCoupon,
    selectCouponError,
    selectIsApplyingCoupon,
    selectIsCartEmpty
} from '@/store';
import {
    clearPendingSmartOfferRedeemToken,
    getPendingSmartOfferRedeemToken,
    SMART_OFFER_REDEEM_EVENT,
} from '@/storefront/services/smartOfferCouponBridge';
import './CouponInput.css';

/**
 * Props for CouponInput component
 */
interface CouponInputProps {
    /** Optional callback when coupon is successfully applied */
    onCouponApplied?: () => void;
    /** Optional callback when coupon is removed */
    onCouponRemoved?: () => void;
}

/**
 * Tag icon SVG
 */
const TagIcon = ({ size = 16, className = '' }: { size?: number; className?: string }) => (
    <svg
        className={className}
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
    >
        <path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" />
        <line x1="7" y1="7" x2="7.01" y2="7" />
    </svg>
);

/**
 * X/Close icon SVG
 */
const XIcon = ({ size = 14 }: { size?: number }) => (
    <svg
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
    >
        <line x1="18" y1="6" x2="6" y2="18" />
        <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
);

/**
 * Check/Success icon SVG
 */
const CheckIcon = ({ size = 14, className = '' }: { size?: number; className?: string }) => (
    <svg
        className={className}
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
    >
        <polyline points="20 6 9 17 4 12" />
    </svg>
);

/**
 * Alert/Error icon SVG
 */
const AlertIcon = ({ size = 14 }: { size?: number }) => (
    <svg
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
    >
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="8" x2="12" y2="12" />
        <line x1="12" y1="16" x2="12.01" y2="16" />
    </svg>
);

/**
 * Loading spinner SVG
 */
const Spinner = ({ size = 16, className = '' }: { size?: number; className?: string }) => (
    <svg
        className={`animate-spin ${className}`}
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
    >
        <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
        />
        <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
        />
    </svg>
);

/**
 * CouponInput - Coupon code input with apply/remove functionality
 */
export const CouponInput = ({ onCouponApplied, onCouponRemoved }: CouponInputProps) => {
    const { t, isRTL } = useTranslation();

    // PHASE 10.4.Y: Currency from useCurrency hook (MongoDB projection via StoreConfig)
    // NO hardcoded currency - throws CurrencyNotLoadedError if currency is missing
    const { formatPrice } = useCurrency();

    // Local state for input
    const [code, setCode] = useState('');
    const [showSuccess, setShowSuccess] = useState(false);

    // Store state
    const appliedCoupon = useCartStore(selectAppliedCoupon);
    const couponError = useCartStore(selectCouponError);
    const isApplying = useCartStore(selectIsApplyingCoupon);
    const isCartEmpty = useCartStore(selectIsCartEmpty);

    // Actions
    const applyCoupon = useCartStore((state) => state.applyCoupon);
    const removeCoupon = useCartStore((state) => state.removeCoupon);
    const clearCouponError = useCartStore((state) => state.clearCouponError);

    const applySelectedCoupon = useCallback(async (selectedCode: string) => {
        const normalizedCode = selectedCode.trim().toUpperCase();
        if (!normalizedCode || isApplying || isCartEmpty) {
            return false;
        }

        clearCouponError();

        if (appliedCoupon && appliedCoupon.code !== normalizedCode) {
            const removed = await removeCoupon();
            if (!removed) {
                return false;
            }
        }

        const success = await applyCoupon(normalizedCode);

        if (success) {
            setCode('');
            setShowSuccess(true);
            onCouponApplied?.();
            window.setTimeout(() => setShowSuccess(false), 3000);
        }

        return success;
    }, [
        appliedCoupon,
        applyCoupon,
        clearCouponError,
        isApplying,
        isCartEmpty,
        onCouponApplied,
        removeCoupon,
    ]);

    const applySelectedSmartOffer = useCallback(async (redeemToken: string) => {
        const normalizedRedeemToken = redeemToken.trim();
        if (!normalizedRedeemToken || isApplying || isCartEmpty) {
            return false;
        }

        clearCouponError();

        if (appliedCoupon) {
            const removed = await removeCoupon();
            if (!removed) {
                return false;
            }
        }

        const success = await applyCoupon(null, normalizedRedeemToken);

        if (success) {
            setCode('');
            setShowSuccess(true);
            onCouponApplied?.();
            window.setTimeout(() => setShowSuccess(false), 3000);
        }

        return success;
    }, [
        appliedCoupon,
        applyCoupon,
        clearCouponError,
        isApplying,
        isCartEmpty,
        onCouponApplied,
        removeCoupon,
    ]);

    /**
     * Handle form submission
     */
    const handleSubmit = useCallback(async (e?: FormEvent) => {
        e?.preventDefault();

        await applySelectedCoupon(code);
    }, [applySelectedCoupon, code]);

    /**
     * Handle keyboard submit
     */
    const handleKeyDown = useCallback((e: KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            handleSubmit();
        }
    }, [handleSubmit]);

    /**
     * Handle coupon removal
     */
    const handleRemove = useCallback(async () => {
        if (isApplying) return;

        const success = await removeCoupon();

        if (success) {
            onCouponRemoved?.();
        }
    }, [isApplying, removeCoupon, onCouponRemoved]);

    /**
     * Handle input change
     */
    const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setCode(e.target.value);
        // Clear error when user starts typing
        if (couponError) {
            clearCouponError();
        }
    }, [couponError, clearCouponError]);

    useEffect(() => {
        let cancelled = false;

        const tryApplyPendingSmartOffer = async () => {
            const pendingRedeemToken = getPendingSmartOfferRedeemToken()?.trim();
            if (!pendingRedeemToken || cancelled || isApplying || isCartEmpty) {
                return;
            }

            try {
                const success = await applySelectedSmartOffer(pendingRedeemToken);
                // Only clear the pending token if the apply succeeded
                if (success) {
                    clearPendingSmartOfferRedeemToken();
                }
                // On failure, leave the token intact for retry
            } catch (error) {
                // On error, leave the token intact for retry
                console.error('Failed to apply pending smart offer:', error);
            }
        };

        const handleSmartOfferRedeemSelected = () => {
            // Event is signal-only - read token securely via getter
            void tryApplyPendingSmartOffer();
        };

        void tryApplyPendingSmartOffer();
        window.addEventListener(SMART_OFFER_REDEEM_EVENT, handleSmartOfferRedeemSelected);

        return () => {
            cancelled = true;
            window.removeEventListener(SMART_OFFER_REDEEM_EVENT, handleSmartOfferRedeemSelected);
        };
    }, [applySelectedSmartOffer, isApplying, isCartEmpty]);

    /**
     * Get localized error message
     */
    const getErrorMessage = useCallback((errorCode: string): string => {
        const translationKey = `cart.coupon.errors.${errorCode}`;
        const translated = t(translationKey);
        // Fallback if translation key doesn't exist
        return translated === translationKey
            ? t('cart.coupon.errors.COUPON_INVALID')
            : translated;
    }, [t]);

    // If coupon is already applied, show the applied coupon badge
    if (appliedCoupon) {
        return (
            <div className={`coupon-applied ${isRTL ? 'rtl' : ''}`}>
                <div className="coupon-applied__badge">
                    <TagIcon size={16} className="coupon-applied__icon" />
                    <span className="coupon-applied__code">{appliedCoupon.code}</span>
                    <span className="coupon-applied__discount">
                        {appliedCoupon.type === 'percentage'
                            ? `${appliedCoupon.value}%`
                            : formatPrice(appliedCoupon.value)}
                    </span>
                    <button
                        type="button"
                        className="coupon-applied__remove"
                        onClick={handleRemove}
                        disabled={isApplying}
                        aria-label={t('cart.coupon.remove')}
                    >
                        {isApplying ? (
                            <Spinner size={14} className="coupon-applied__spinner" />
                        ) : (
                            <XIcon size={14} />
                        )}
                    </button>
                </div>
                <div className="coupon-applied__savings">
                    <CheckIcon size={14} className="coupon-applied__check" />
                    <span>
                        {t('cart.coupon.savedAmount', { amount: formatPrice(appliedCoupon.amount) })}
                    </span>
                </div>
            </div>
        );
    }

    // Render input form
    return (
        <div className={`coupon-input ${isRTL ? 'rtl' : ''}`}>
            <div className="coupon-input__header">
                <TagIcon size={16} className="coupon-input__icon" />
                <span className="coupon-input__title">{t('cart.coupon.title')}</span>
            </div>

            <form className="coupon-input__form" onSubmit={handleSubmit}>
                <div className="coupon-input__field-wrapper">
                    <input
                        type="text"
                        className={`coupon-input__field ${couponError ? 'error' : ''}`}
                        placeholder={t('cart.coupon.placeholder')}
                        value={code}
                        onChange={handleInputChange}
                        onKeyDown={handleKeyDown}
                        disabled={isApplying || isCartEmpty}
                        autoComplete="off"
                        autoCapitalize="characters"
                        spellCheck={false}
                        aria-label={t('cart.coupon.placeholder')}
                        aria-invalid={!!couponError}
                        aria-describedby={couponError ? 'coupon-error' : undefined}
                    />
                    <button
                        type="submit"
                        className="coupon-input__button"
                        disabled={!code.trim() || isApplying || isCartEmpty}
                    >
                        {isApplying ? (
                            <>
                                <Spinner size={16} className="coupon-input__spinner" />
                                <span>{t('cart.coupon.applying')}</span>
                            </>
                        ) : (
                            <span>{t('cart.coupon.apply')}</span>
                        )}
                    </button>
                </div>

                {/* Error message */}
                {couponError && (
                    <div className="coupon-input__error" id="coupon-error" role="alert">
                        <AlertIcon size={14} />
                        <span>{getErrorMessage(couponError)}</span>
                    </div>
                )}

                {/* Success message */}
                {showSuccess && !couponError && (
                    <div className="coupon-input__success" role="status">
                        <CheckIcon size={14} />
                        <span>{t('cart.coupon.applied')}</span>
                    </div>
                )}
            </form>
        </div>
    );
};

export default CouponInput;
