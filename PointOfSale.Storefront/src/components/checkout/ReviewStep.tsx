/**
 * Review Step Component
 * 
 * Final step of checkout: Review order and place order.
 * Displays all order details before submission.
 * 
 * Phase 7.7: Checkout UI Implementation
 * Phase 7.7.1: UX Polish - Submission feedback, confidence boost, error recovery
 */

import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from '@/i18n';
import { PhoneOtpVerification } from './PhoneOtpVerification';
import {
    useCheckoutStore,
    selectShippingAddress,
    selectSelectedShippingMethod,
    selectIsSubmitting,
    selectCheckoutError,
    selectErrorCode,
    selectPricingPreview,
    selectPricingLoading,
    selectPricingError,
    selectShippingSelectionStatus,
    selectStaleShippingSelection,
    selectPaymentSelectionStatus,
    selectStalePaymentSelection,
    selectSelectedPaymentMethod,
    selectCheckoutFields,
    selectCustomFieldValues,
} from '@/store/checkoutStore';
import {
    useCartStore,
    selectCartCurrency,
    selectCartItemCount,
    selectAppliedCoupon,
} from '@/store';
import { formatMoney } from '@/utils';
import { getCustomCheckoutFieldReviewItems } from '@/utils/checkoutFieldRules';

// Constants
const STALE_SELECTION_FALLBACK = 'Your previous selection is no longer available';

interface ReviewStepProps {
    onPlaceOrder: () => Promise<void>;
    onBack: () => void;
}

/**
 * Summary Section Component
 */
interface SummarySectionProps {
    title: string;
    onEdit?: () => void;
    editDisabled?: boolean;
    children: React.ReactNode;
}

const SummarySection: React.FC<SummarySectionProps> = ({ title, onEdit, editDisabled, children }) => (
    <div className="p-4 sm:p-5 bg-muted/5 rounded-xl">
        <div className="flex items-center justify-between mb-3">
            <h3 className="font-medium text-text">{title}</h3>
            {onEdit && (
                <button
                    onClick={onEdit}
                    disabled={editDisabled}
                    className={`
                        text-sm text-primary hover:underline 
                        focus-visible:ring-2 focus-visible:ring-primary rounded
                        ${editDisabled ? 'opacity-50 cursor-not-allowed' : ''}
                    `}
                >
                    Edit
                </button>
            )}
        </div>
        {children}
    </div>
);

/**
 * Submission Status Messages
 * Rotate through friendly status messages during submission
 */
const SUBMISSION_MESSAGES = [
    'Placing your order…',
    'Securing inventory…',
    'Confirming payment method…',
    'Almost there…',
];

/**
 * Confidence Boost Component
 * Shows reassuring checkmarks before placing order
 */
const ConfidenceBoost: React.FC = () => (
    <div className="p-4 bg-green-50/50 border border-green-100 rounded-xl space-y-2">
        <div className="flex items-center gap-2 text-sm text-green-700">
            <span className="text-green-600">✓</span>
            <span>Prices confirmed at checkout</span>
        </div>
        <div className="flex items-center gap-2 text-sm text-green-700">
            <span className="text-green-600">✓</span>
            <span>Shipping reserved for your order</span>
        </div>
        <div className="flex items-center gap-2 text-sm text-green-700">
            <span className="text-green-600">✓</span>
            <span>You will not be charged twice</span>
        </div>
    </div>
);

/**
 * Error Helper Component
 * Provides contextual help for different error types
 */
interface ErrorHelperProps {
    errorCode: string | null;
    error: string;
    onDismiss: () => void;
}

const ErrorHelper: React.FC<ErrorHelperProps> = ({ errorCode, error, onDismiss }) => {
    const { t } = useTranslation();
    
    // Get contextual help based on error code
    const getErrorHelp = () => {
        if (errorCode?.startsWith('COUPON_')) {
            return {
                title: 'Coupon needs attention',
                message: error,
                help: 'The coupon on this checkout is no longer valid for the latest backend cart. Please review the cart totals and apply a valid coupon before retrying.',
                action: { label: 'Review Cart', href: '/cart' },
                variant: 'warning' as const,
            };
        }

        switch (errorCode) {
            case 'OUT_OF_STOCK':
                return {
                    title: 'Some items are no longer available',
                    message: error,
                    help: 'One or more items in your cart have sold out. Please review your cart and remove or replace the unavailable items.',
                    action: { label: 'Review Cart', href: '/cart' },
                    variant: 'warning' as const,
                };
            case 'PRICE_CHANGED':
                return {
                    title: 'Prices have been updated',
                    message: error,
                    help: 'The prices of some items have changed since you added them. Please review the updated totals before placing your order.',
                    action: { label: 'Review Cart', href: '/cart' },
                    variant: 'warning' as const,
                };
            case 'NETWORK_ERROR':
                return {
                    title: 'Connection issue',
                    message: error,
                    help: 'Please check your internet connection and try again. Review your details before retrying in case they have not synced yet.',
                    variant: 'error' as const,
                };
            case 'PAYMENT_CONFIRMATION_FAILED':
                return {
                    title: 'Payment confirmation failed',
                    message: error,
                    help: 'Your order was created, but the backend could not confirm the payment state yet. Retry the checkout flow or contact support before assuming the payment succeeded.',
                    variant: 'error' as const,
                };
            case 'PHONE_VERIFICATION_REQUIRED':
                return {
                    title: t('checkout.errors.phoneNotVerified.title'),
                    message: t('checkout.errors.phoneNotVerified.message'),
                    help: t('checkout.errors.phoneNotVerified.help'),
                    variant: 'warning' as const,
                };
            case 'PHONE_BLOCKED':
                return {
                    title: t('checkout.errors.phoneBlocked.title'),
                    message: t('checkout.errors.phoneBlocked.message'),
                    help: t('checkout.errors.phoneBlocked.help'),
                    variant: 'error' as const,
                };
            case 'RECOVERY_REVIEW_REQUIRED':
                return {
                    title: t('checkout.errors.recoveryReviewRequired.title'),
                    message: t('checkout.errors.recoveryReviewRequired.message'),
                    help: t('checkout.errors.recoveryReviewRequired.help'),
                    variant: 'warning' as const,
                };
            case 'RECOVERY_CART_CHANGED':
                return {
                    title: t('checkout.errors.recoveryCartChanged.title'),
                    message: t('checkout.errors.recoveryCartChanged.message'),
                    help: t('checkout.errors.recoveryCartChanged.help'),
                    variant: 'warning' as const,
                };
            case 'MANUAL_WALLET_TYPE_REQUIRED':
            case 'MANUAL_WALLET_TYPE_INVALID':
            case 'MANUAL_WALLET_PHONE_REQUIRED':
            case 'INVALID_MANUAL_WALLET_PHONE':
            case 'MANUAL_WALLET_PROOF_REQUIRED':
                return {
                    title: t('checkout.errors.manualWalletIncomplete.title'),
                    message: t('checkout.errors.manualWalletIncomplete.message'),
                    help: t('checkout.errors.manualWalletIncomplete.help'),
                    variant: 'warning' as const,
                };
            default:
                return {
                    title: t('checkout.errors.generic.title'),
                    message: (typeof error === 'string' && error.trim()) 
                        ? error 
                        : t('checkout.errors.generic.message'),
                    help: t('checkout.errors.generic.help'),
                    variant: 'error' as const,
                };
        }
    };

    const errorInfo = getErrorHelp();
    const isWarning = errorInfo.variant === 'warning';

    return (
        <div
            className={`
                p-4 rounded-xl
                ${isWarning
                    ? 'bg-amber-50 border border-amber-200'
                    : 'bg-red-50 border border-red-200'
                }
            `}
            role="alert"
            aria-live="assertive"
        >
            <div className="flex items-start gap-3">
                <svg
                    className={`w-5 h-5 mt-0.5 flex-shrink-0 ${isWarning ? 'text-amber-500' : 'text-red-500'}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
                <div className="flex-1 space-y-2">
                    <p className={`font-medium ${isWarning ? 'text-amber-800' : 'text-red-800'}`}>
                        {errorInfo.title}
                    </p>
                    <p className={`text-sm ${isWarning ? 'text-amber-800' : 'text-red-800'}`}>
                        {errorInfo.message}
                    </p>
                    <p className={`text-sm ${isWarning ? 'text-amber-700' : 'text-red-700'}`}>
                        {errorInfo.help}
                    </p>
                    {errorInfo.action && (
                        <a
                            href={errorInfo.action.href}
                            className="inline-block mt-1 text-sm text-primary hover:underline font-medium"
                        >
                            {errorInfo.action.label} →
                        </a>
                    )}
                </div>
                <button
                    onClick={onDismiss}
                    className={`p-1 transition-colors ${isWarning ? 'text-amber-400 hover:text-amber-600' : 'text-red-400 hover:text-red-600'}`}
                    aria-label="Dismiss error"
                >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                </button>
            </div>
        </div>
    );
};

/**
 * Review Step Component
 * 
 * ARCHITECTURAL RULE: Checkout displays ONLY backend pricing quotes.
 * - Shipping, tax, discounts, and total come from the pricing preview endpoint
 * - Frontend never recomputes monetary values
 */
export const ReviewStep: React.FC<ReviewStepProps> = ({ onPlaceOrder, onBack }) => {
    const { t } = useTranslation();

    // Store state
    const address = useCheckoutStore(selectShippingAddress);
    const shippingMethod = useCheckoutStore(selectSelectedShippingMethod);
    const selectedPaymentMethod = useCheckoutStore(selectSelectedPaymentMethod);
    const isSubmitting = useCheckoutStore(selectIsSubmitting);
    const error = useCheckoutStore(selectCheckoutError);
    const errorCode = useCheckoutStore(selectErrorCode);
    const pricing = useCheckoutStore(selectPricingPreview);
    const pricingLoading = useCheckoutStore(selectPricingLoading);
    const pricingError = useCheckoutStore(selectPricingError);
    const isReviewValid = useCheckoutStore((state) => state.isStepValid('review'));
    const shippingSelectionStatus = useCheckoutStore(selectShippingSelectionStatus);
    const staleShippingSelection = useCheckoutStore(selectStaleShippingSelection);
    const paymentSelectionStatus = useCheckoutStore(selectPaymentSelectionStatus);
    const stalePaymentSelection = useCheckoutStore(selectStalePaymentSelection);
    const checkoutFields = useCheckoutStore(selectCheckoutFields);
    const customFieldValues = useCheckoutStore(selectCustomFieldValues);
    const customFieldReviewItems = getCustomCheckoutFieldReviewItems(checkoutFields, address, customFieldValues);

    const currency = useCartStore(selectCartCurrency);
    const itemCount = useCartStore(selectCartItemCount);
    const appliedCoupon = useCartStore(selectAppliedCoupon);

    // Store actions
    const goToStep = useCheckoutStore((state) => state.goToStep);
    const resetError = useCheckoutStore((state) => state.resetError);
    const refreshPricingPreview = useCheckoutStore((state) => state.refreshPricingPreview);

    // Submission status message rotation
    const [statusMessageIndex, setStatusMessageIndex] = useState(0);

    // Phone verification state
    const [isPhoneVerified, setIsPhoneVerified] = useState(false);

    // Callback when phone is verified
    const handlePhoneVerified = useCallback(() => {
        setIsPhoneVerified(true);
    }, []);

    useEffect(() => {
        setIsPhoneVerified(false);
    }, [address.phone]);

    useEffect(() => {
        if (isSubmitting) {
            const interval = setInterval(() => {
                setStatusMessageIndex((prev) => (prev + 1) % SUBMISSION_MESSAGES.length);
            }, 2000);
            return () => clearInterval(interval);
        } else {
            setStatusMessageIndex(0);
        }
    }, [isSubmitting]);

    // Handle place order
    const handlePlaceOrder = async () => {
        resetError();
        await onPlaceOrder();
    };

    const handleRetryPricing = useCallback(() => {
        void refreshPricingPreview();
    }, [refreshPricingPreview]);

    // Handle edit navigation (disabled during submission)
    const handleEdit = (step: 'shipping' | 'payment') => {
        if (!isSubmitting) {
            goToStep(step);
        }
    };

    return (
        <div className="space-y-6">
            {/* Error Banner with contextual help */}
            {error && (
                <ErrorHelper
                    errorCode={errorCode}
                    error={error}
                    onDismiss={resetError}
                />
            )}

            {/* Shipping Address Summary */}
            <SummarySection
                title="Shipping Address"
                onEdit={() => handleEdit('shipping')}
                editDisabled={isSubmitting}
            >
                <div className="text-sm text-text space-y-1">
                    <p className="font-medium">{address.fullName}</p>
                    <p className="text-muted">{address.phone}</p>
                    <p className="text-muted">
                        {address.addressLine}
                        {address.area && `, ${address.area}`}
                        {address.city && `, ${address.city}`}
                    </p>
                    {address.notes && (
                        <p className="text-muted italic text-xs mt-2">
                            Note: {address.notes}
                        </p>
                    )}
                </div>
            </SummarySection>

            {customFieldReviewItems.length > 0 && (
                <SummarySection
                    title="Additional details"
                    onEdit={() => handleEdit('shipping')}
                    editDisabled={isSubmitting}
                >
                    <dl className="space-y-3 text-sm">
                        {customFieldReviewItems.map((item) => (
                            <div key={item.id}>
                                <dt className="text-xs font-medium text-muted">{item.label}</dt>
                                <dd className="mt-1 whitespace-pre-wrap text-text">{item.value}</dd>
                            </div>
                        ))}
                    </dl>
                </SummarySection>
            )}

            {/* Shipping Method Summary */}
            <SummarySection
                title="Shipping Method"
                onEdit={() => handleEdit('shipping')}
                editDisabled={isSubmitting}
            >
                {shippingMethod ? (
                    <div className="flex items-center justify-between text-sm">
                        <div>
                            <p className="font-medium text-text">{shippingMethod.name}</p>
                            {shippingMethod.estimatedDays && (
                                <p className="text-muted text-xs">
                                    Est. {shippingMethod.estimatedDays} day{shippingMethod.estimatedDays !== 1 ? 's' : ''}
                                </p>
                            )}
                        </div>
                        <p className="font-medium text-text">
                            {pricing
                                ? (pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free')
                                : 'Calculating...'}
                        </p>
                    </div>
                ) : shippingSelectionStatus === 'stale' && staleShippingSelection ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-3 text-sm text-amber-800">
                        <p className="font-medium">{t(staleShippingSelection.message, { defaultValue: STALE_SELECTION_FALLBACK })}</p>
                        <p className="mt-1 text-amber-700">
                            {t('checkout.previousSelection')}: {staleShippingSelection.label}
                        </p>
                    </div>
                ) : (
                    <p className="text-sm text-muted">No shipping method selected</p>
                )}
            </SummarySection>

            {/* Payment Method Summary */}
            <SummarySection
                title={t('checkout.paymentMethod')}
                onEdit={() => handleEdit('payment')}
                editDisabled={isSubmitting}
            >
                <div className="flex items-center gap-3 text-sm">
                    {selectedPaymentMethod ? (
                        <>
                            <div className="w-10 h-10 rounded-lg bg-primary/10 flex items-center justify-center">
                                <svg className="w-5 h-5 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
                                </svg>
                            </div>
                            <div className="flex-1">
                                <p className="font-medium text-text">{selectedPaymentMethod.displayName}</p>
                                {selectedPaymentMethod.description && (
                                    <p className="text-muted text-xs">{selectedPaymentMethod.description}</p>
                                )}
                            </div>
                            {selectedPaymentMethod.supportsOffline && (
                                <span className="px-2 py-1 text-xs font-medium bg-amber-100 text-amber-700 rounded-full">
                                    {t('checkout.payment.cod.badge')}
                                </span>
                            )}
                        </>
                    ) : paymentSelectionStatus === 'stale' && stalePaymentSelection ? (
                        <div className="w-full rounded-lg border border-amber-200 bg-amber-50 px-3 py-3 text-sm text-amber-800">
                            <p className="font-medium">{t(stalePaymentSelection.message, { defaultValue: STALE_SELECTION_FALLBACK })}</p>
                            <p className="mt-1 text-amber-700">
                                {t('checkout.previousSelection')}: {stalePaymentSelection.label}
                            </p>
                        </div>
                    ) : (
                        <p className="text-muted">{t('checkout.payment.noMethods')}</p>
                    )}
                </div>
            </SummarySection>

            {/* Order Total */}
            <div className="p-4 sm:p-5 bg-secondary/5 rounded-xl border border-secondary/10">
                {pricingLoading && !pricing ? (
                    <div className="rounded-lg bg-muted/5 px-3 py-4 text-sm text-muted">
                        Calculating server-side totals...
                    </div>
                ) : pricing ? (
                    <>
                        <div className="space-y-2 mb-4">
                            <div className="flex justify-between text-sm">
                                <span className="text-muted">Subtotal ({itemCount} item{itemCount !== 1 ? 's' : ''})</span>
                                <span className="text-text">{formatMoney(pricing.subtotal, currency)}</span>
                            </div>

                            {pricing.promotionDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-red-500 flex items-center gap-1">
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
                                        </svg>
                                        Promotion Savings
                                    </span>
                                    <span className="text-red-500 font-medium">
                                        -{formatMoney(pricing.promotionDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            {pricing.cartPromotionDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-orange-600">Cart Promotion</span>
                                    <span className="text-orange-600 font-medium">
                                        -{formatMoney(pricing.cartPromotionDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            {pricing.couponDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-green-600">
                                        Coupon
                                        {appliedCoupon && (
                                            <span className="text-xs ml-1">({appliedCoupon.code})</span>
                                        )}
                                    </span>
                                    <span className="text-green-600">
                                        -{formatMoney(pricing.couponDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            <div className="flex justify-between text-sm">
                                <span className="text-muted">Shipping</span>
                                <span className="text-text">
                                    {pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : 'Free'}
                                </span>
                            </div>

                            <div className="flex justify-between text-sm">
                                <span className="text-muted">Tax</span>
                                <span className="text-text">{formatMoney(pricing.tax, currency)}</span>
                            </div>

                            {pricing.paymentFee > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-muted">Payment Fee</span>
                                    <span className="text-text">{formatMoney(pricing.paymentFee, currency)}</span>
                                </div>
                            )}
                        </div>
                        <hr className="border-muted/10 mb-4" />
                        <div className="flex justify-between">
                            <span className="font-semibold text-text">Total</span>
                            <span className="font-bold text-xl text-text">{formatMoney(pricing.total, currency)}</span>
                        </div>
                    </>
                ) : (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-4 text-sm text-amber-800">
                        <p>{pricingError || 'Unable to load the latest checkout pricing.'}</p>
                        <p className="mt-1 text-amber-700">
                            Retry to confirm the current backend totals before placing the order.
                        </p>
                        <button
                            type="button"
                            onClick={handleRetryPricing}
                            className="mt-3 inline-flex items-center rounded-lg border border-amber-300 px-3 py-2 font-medium text-amber-900 hover:bg-amber-100"
                        >
                            Retry totals
                        </button>
                    </div>
                )}
            </div>

            {/* Phone OTP Verification - WhatsApp-based phone verification */}
            {/* Only shown when required by tenant configuration */}
            {!isPhoneVerified && address.phone && (
                <PhoneOtpVerification
                    phone={address.phone}
                    onVerified={handlePhoneVerified}
                    disabled={isSubmitting}
                />
            )}

            {/* Confidence Boost - only show when not submitting and no error */}
            {!isSubmitting && !error && isPhoneVerified && (
                <ConfidenceBoost />
            )}

            {/* Navigation Buttons */}
            <div className="flex flex-col sm:flex-row gap-3 pt-4">
                <button
                    onClick={onBack}
                    disabled={isSubmitting}
                    className="
                        py-3 px-6
                        rounded-xl font-medium
                        border border-muted/20 text-text
                        hover:bg-muted/10
                        transition-colors
                        disabled:opacity-50 disabled:cursor-not-allowed
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        order-2 sm:order-1
                    "
                >
                    Back to Payment
                </button>

                <div className="flex-1 order-1 sm:order-2">
                    <button
                        onClick={handlePlaceOrder}
                        disabled={isSubmitting || !isPhoneVerified || pricingLoading || !pricing || !isReviewValid}
                        className="
                            w-full py-3.5 px-6
                            rounded-xl font-medium
                            bg-primary text-white
                            hover:bg-primary/90
                            shadow-md hover:shadow-lg
                            transition-all duration-200
                            disabled:opacity-70 disabled:cursor-not-allowed
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                            flex items-center justify-center gap-2
                        "
                        aria-describedby={(!isPhoneVerified || pricingLoading || !pricing || !isReviewValid) ? "place-order-help" : undefined}
                    >
                        {isSubmitting ? (
                            <>
                                <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                </svg>
                                {t('checkout.button.processing')}
                            </>
                        ) : (
                            <>
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                                </svg>
                                {t('checkout.button.placeOrder')}
                            </>
                        )}
                    </button>
                    
                    {/* Helper text for disabled state - accessible to screen readers and visible to all users */}
                    {(!isPhoneVerified || pricingLoading || !pricing || !isReviewValid) && !isSubmitting && (
                        <p
                            id="place-order-help"
                            className="mt-2 text-sm text-muted text-center"
                            role="status"
                            aria-live="polite"
                        >
                            {!isPhoneVerified 
                                ? t('checkout.button.tooltips.verifyPhone')
                                : pricingLoading
                                ? t('checkout.button.tooltips.waitingPricing')
                                : shippingSelectionStatus === 'stale' || paymentSelectionStatus === 'stale'
                                ? t('checkout.staleSelectionHelp')
                                : !pricing 
                                ? t('checkout.button.tooltips.waitingPricing') 
                                : t('checkout.button.tooltips.completeDetails')}
                        </p>
                    )}
                </div>
            </div>

            {/* Submission Status Text - shows below button during submission */}
            {isSubmitting && (
                <p
                    className="text-center text-sm text-muted animate-pulse"
                    aria-live="polite"
                    aria-atomic="true"
                >
                    {SUBMISSION_MESSAGES[statusMessageIndex]}
                </p>
            )}

            {/* Security Note */}
            {!isSubmitting && (
                <p className="text-center text-xs text-muted">
                    <svg className="inline w-3.5 h-3.5 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                    </svg>
                    Your information is secure and encrypted
                </p>
            )}
        </div>
    );
};
