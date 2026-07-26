/**
 * Payment Step Component
 * 
 * PHASE 10.4.D2: Payment Methods UI
 * 
 * Second step of checkout: Payment method selection.
 * 
 * ARCHITECTURAL RULES:
 * - Payment methods are fetched from POS Admin via Storefront API
 * - NO hardcoded payment methods
 * - NO assumptions about available methods
 * - NO payment execution in this phase
 * - Display only what POS Admin enables
 */

import React, { useEffect } from 'react';
import { useTranslation } from '@/i18n';
import { useCurrency } from '@/hooks';
import {
    useCheckoutStore,
    selectPaymentMethods,
    selectSelectedPaymentMethodCode,
    selectPaymentMethodsLoading,
    selectPaymentMethodsError,
    selectPaymentFee,
    selectPricingPreview,
    selectPaymentSelectionStatus,
    selectStalePaymentSelection,
    selectSelectedPaymentMethod,
} from '@/store/checkoutStore';
import {
    ManualWalletFields,
} from '@/components/checkout';
import { useCartStore, selectCartSubtotal } from '@/store/cartStore';
import type { PaymentMethodDto } from '@/types/checkout';

interface PaymentStepProps {
    onContinue: () => void;
    onBack: () => void;
}

// ============================================================================
// Payment Method Card Component
// ============================================================================

interface PaymentMethodCardProps {
    method: PaymentMethodDto;
    isSelected: boolean;
    isDisabled: boolean;
    disabledReason?: string;
    onSelect: () => void;
    isRtl: boolean;
    formatPrice: (amount: number) => string;
}

/**
 * Get icon for payment method
 * PHASE P3: All canonical payment methods must have icons
 */
const getPaymentIcon = (code: string): React.ReactNode => {
    switch (code) {
        case 'COD':
            return (
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 9V7a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2m2 4h10a2 2 0 002-2v-6a2 2 0 00-2-2H9a2 2 0 00-2 2v6a2 2 0 002 2zm7-5a2 2 0 11-4 0 2 2 0 014 0z" />
                </svg>
            );
        case 'FAWRY':
            return (
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 7h6m-6 4h6m-6 4h6M5 3h14a2 2 0 012 2v14a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2z" />
                </svg>
            );
        case 'MANUALWALLET':
            return (
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 18h.01M8 21h8a2 2 0 002-2V5a2 2 0 00-2-2H8a2 2 0 00-2 2v14a2 2 0 002 2z" />
                </svg>
            );
        case 'STRIPE':
        case 'PAYMOB':
        default:
            return (
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
                </svg>
            );
    }
};

const PaymentMethodCard: React.FC<PaymentMethodCardProps> = ({
    method,
    isSelected,
    isDisabled,
    disabledReason,
    onSelect,
    isRtl,
    formatPrice,
}) => {
    // Localized display name and description
    const displayName = isRtl ? (method.displayNameAr || method.displayName) : method.displayName;
    const description = isRtl ? (method.descriptionAr || method.description) : method.description;

    // Format fee display
    const feeDisplay = method.fee > 0
        ? method.feeType === 'percentage'
            ? `${method.fee}%`
            : formatPrice(method.fee)
        : null;

    return (
        <label
            className={`
                flex items-start gap-4 p-4 sm:p-5
                border rounded-xl
                transition-all duration-200
                ${isDisabled
                    ? 'opacity-50 cursor-not-allowed border-muted/10 bg-muted/5'
                    : 'cursor-pointer'
                }
                ${!isDisabled && isSelected
                    ? 'border-primary bg-primary/5 ring-2 ring-primary/20'
                    : !isDisabled
                        ? 'border-muted/20 hover:border-muted/40 hover:bg-muted/5'
                        : ''
                }
            `}
        >
            <input
                type="radio"
                name="paymentMethod"
                value={method.code}
                checked={isSelected}
                onChange={onSelect}
                disabled={isDisabled}
                className="sr-only"
            />

            {/* Custom radio indicator */}
            <div
                className={`
                    flex-shrink-0 w-5 h-5 mt-0.5
                    rounded-full border-2
                    transition-colors
                    ${isSelected && !isDisabled
                        ? 'border-primary bg-primary'
                        : 'border-muted/30'
                    }
                `}
            >
                {isSelected && !isDisabled && (
                    <svg className="w-full h-full text-white p-0.5" fill="currentColor" viewBox="0 0 24 24">
                        <circle cx="12" cy="12" r="6" />
                    </svg>
                )}
            </div>

            {/* Icon */}
            <div className={`
                flex-shrink-0 w-12 h-12
                flex items-center justify-center
                rounded-xl
                ${isSelected && !isDisabled ? 'bg-primary/10 text-primary' : 'bg-muted/10 text-muted'}
            `}>
                {getPaymentIcon(method.code)}
            </div>

            {/* Content */}
            <div className="flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                    <span className={`font-medium ${isDisabled ? 'text-muted' : 'text-text'}`}>
                        {displayName}
                    </span>
                    {method.supportsOffline && (
                        <span className="px-2 py-0.5 text-xs font-medium bg-amber-100 text-amber-700 rounded-full">
                            {isRtl ? 'الدفع عند الاستلام' : 'Pay on Delivery'}
                        </span>
                    )}
                    {feeDisplay && (
                        <span className="px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-700 rounded-full">
                            {isRtl ? 'رسوم' : 'Fee'}: {feeDisplay}
                        </span>
                    )}
                </div>
                {description && (
                    <p className={`text-sm mt-0.5 ${isDisabled ? 'text-muted/60' : 'text-muted'}`}>
                        {description}
                    </p>
                )}
                {isDisabled && disabledReason && (
                    <p className="text-sm text-red-500 mt-1 flex items-center gap-1">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                        </svg>
                        {disabledReason}
                    </p>
                )}
            </div>
        </label>
    );
};

// ============================================================================
// Loading Skeleton
// ============================================================================

const PaymentMethodSkeleton: React.FC = () => (
    <div className="space-y-3">
        {[1, 2].map((i) => (
            <div key={i} className="flex items-start gap-4 p-4 sm:p-5 border border-muted/20 rounded-xl animate-pulse">
                <div className="w-5 h-5 rounded-full bg-muted/20" />
                <div className="w-12 h-12 rounded-xl bg-muted/20" />
                <div className="flex-1 space-y-2">
                    <div className="h-4 bg-muted/20 rounded w-1/3" />
                    <div className="h-3 bg-muted/20 rounded w-2/3" />
                </div>
            </div>
        ))}
    </div>
);

// ============================================================================
// Payment Step Component
// ============================================================================

export const PaymentStep: React.FC<PaymentStepProps> = ({ onContinue, onBack }) => {
    const { t, isRTL } = useTranslation();
    const { formatPrice } = useCurrency();

    // Store state
    const paymentMethods = useCheckoutStore(selectPaymentMethods);
    const selectedCode = useCheckoutStore(selectSelectedPaymentMethodCode);
    const selectedMethod = useCheckoutStore(selectSelectedPaymentMethod);
    const isLoading = useCheckoutStore(selectPaymentMethodsLoading);
    const error = useCheckoutStore(selectPaymentMethodsError);
    const paymentFee = useCheckoutStore(selectPaymentFee);
    const pricing = useCheckoutStore(selectPricingPreview);
    const paymentSelectionStatus = useCheckoutStore(selectPaymentSelectionStatus);
    const stalePaymentSelection = useCheckoutStore(selectStalePaymentSelection);
    const fetchPaymentMethods = useCheckoutStore((state) => state.fetchPaymentMethods);
    const selectPaymentMethod = useCheckoutStore((state) => state.selectPaymentMethod);
    const isStepValid = useCheckoutStore((state) => state.isStepValid);

    // Use the authoritative preview total when pricing has been calculated.
    // Before preview pricing exists, the subtotal remains advisory-only.
    const cartSubtotal = useCartStore(selectCartSubtotal);
    const orderAmountForValidation = pricing?.total ?? cartSubtotal;

    // Fetch payment methods on mount
    useEffect(() => {
        if (paymentMethods.length === 0 && !isLoading) {
            fetchPaymentMethods();
        }
    }, [paymentMethods.length, isLoading, fetchPaymentMethods]);

    /**
     * Check if a method is disabled based on order amount limits
     */
    const getDisabledState = (method: PaymentMethodDto): { isDisabled: boolean; reason?: string } => {
        if (!method.isAvailable) {
            return {
                isDisabled: true,
                reason: isRTL
                    ? 'طريقة الدفع غير متاحة'
                    : 'Payment method unavailable'
            };
        }

        if (method.minOrderAmount && orderAmountForValidation < method.minOrderAmount) {
            return {
                isDisabled: true,
                reason: isRTL
                    ? `الحد الأدنى للطلب: ${formatPrice(method.minOrderAmount)}`
                    : `Minimum order: ${formatPrice(method.minOrderAmount)}`
            };
        }
        if (method.maxOrderAmount && orderAmountForValidation > method.maxOrderAmount) {
            return {
                isDisabled: true,
                reason: isRTL
                    ? `الحد الأقصى للطلب: ${formatPrice(method.maxOrderAmount)}`
                    : `Maximum order: ${formatPrice(method.maxOrderAmount)}`
            };
        }
        return { isDisabled: false };
    };

    const handleContinue = () => {
        const disabled = selectedMethod ? getDisabledState(selectedMethod).isDisabled : true;
        if (!!selectedCode && !disabled && isStepValid('payment')) {
            onContinue();
        }
    };

    const disabled = selectedMethod ? getDisabledState(selectedMethod).isDisabled : true;
    const canContinue = !!selectedCode && !!selectedMethod?.isAvailable && !disabled && isStepValid('payment');

    return (
        <div className="space-y-8">
            {/* Payment Methods */}
            <section>
                <h2 className="text-lg font-semibold text-text mb-4">
                    {t('checkout.paymentMethod')}
                </h2>

                {/* Loading State */}
                {isLoading && <PaymentMethodSkeleton />}

                {/* Error State */}
                {error && !isLoading && (
                    <div className="p-4 bg-red-50 border border-red-200 rounded-xl text-red-700">
                        <p>{error}</p>
                        <button
                            onClick={() => fetchPaymentMethods()}
                            className="mt-2 text-sm underline hover:no-underline"
                        >
                            {t('common.retry')}
                        </button>
                    </div>
                )}

                {/* Empty State */}
                {!isLoading && !error && paymentMethods.length === 0 && (
                    <div className="p-6 bg-muted/5 border border-muted/20 rounded-xl text-center">
                        <svg className="w-12 h-12 mx-auto text-muted mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
                        </svg>
                        <p className="text-muted">
                            {isRTL ? 'لا توجد طرق دفع متاحة' : 'No payment methods available'}
                        </p>
                    </div>
                )}

                {/* Payment Methods List */}
                {!isLoading && !error && paymentMethods.length > 0 && (
                    <div className="space-y-3">
                        {paymentSelectionStatus === 'stale' && stalePaymentSelection && (
                            <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                                <p className="font-medium">{stalePaymentSelection.message}</p>
                                <p className="mt-1 text-amber-700">
                                    {t('checkout.previousSelection')}: {stalePaymentSelection.label}
                                </p>
                            </div>
                        )}
                        {paymentMethods.map((method) => {
                            const { isDisabled, reason } = getDisabledState(method);
                            return (
                                <div key={method.code}>
                                    <PaymentMethodCard
                                        method={method}
                                        isSelected={selectedCode === method.code}
                                        isDisabled={isDisabled}
                                        disabledReason={reason}
                                        onSelect={() => !isDisabled && selectPaymentMethod(method.code)}
                                        isRtl={isRTL}
                                        formatPrice={formatPrice}
                                    />
                                    {/* Render Manual Wallet Fields if this method is selected and is MANUALWALLET */}
                                    {selectedCode === method.code && method.code === 'MANUALWALLET' && (
                                        <ManualWalletFields />
                                    )}
                                </div>
                            );
                        })}
                    </div>
                )}
            </section>

            {/* Selected Payment Fee Notice */}
            {paymentFee > 0 && selectedCode && (
                <div className="flex items-center gap-2 p-4 bg-blue-50 border border-blue-200 rounded-xl text-blue-700">
                    <svg className="w-5 h-5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <p className="text-sm">
                        {isRTL
                            ? `سيتم إضافة رسوم بقيمة ${formatPrice(paymentFee)} إلى طلبك.`
                            : `A fee of ${formatPrice(paymentFee)} will be added to your order.`
                        }
                    </p>
                </div>
            )}

            {/* Navigation Buttons */}
            <div className="flex flex-col sm:flex-row gap-3 pt-4">
                <button
                    onClick={onBack}
                    className="
                        py-3 px-6
                        rounded-xl font-medium
                        border border-muted/20 text-text
                        hover:bg-muted/10
                        transition-colors
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        order-2 sm:order-1
                    "
                >
                    {t('checkout.backToShipping')}
                </button>

                <button
                    onClick={handleContinue}
                    disabled={!canContinue}
                    className={`
                        flex-1 py-3.5 px-6
                        rounded-xl font-medium
                        transition-all duration-200
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        order-1 sm:order-2
                        ${canContinue
                            ? 'bg-primary text-white hover:bg-primary/90 shadow-md hover:shadow-lg'
                            : 'bg-muted/20 text-muted cursor-not-allowed'
                        }
                    `}
                    aria-disabled={!canContinue}
                >
                    {t('checkout.reviewOrder')}
                </button>
            </div>

            {selectedCode === 'MANUALWALLET' && !canContinue && (
                <p className="text-sm text-amber-700">
                    {t('checkout.payment.manualWalletWarning')}
                </p>
            )}
        </div>
    );
};
