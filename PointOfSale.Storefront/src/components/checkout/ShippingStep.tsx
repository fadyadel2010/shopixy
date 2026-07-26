/**
 * Shipping Step Component
 * 
 * First step of checkout: Address form + Shipping method selection.
 * Features:
 * - Real-time validation
 * - Labels above inputs
 * - Inline field errors
 * - Auto-select first shipping method
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
    useCheckoutStore,
    selectShippingMethods,
    selectShippingMethodId,
    selectShippingMethodsLoading,
    selectShippingMethodsError,
    selectShippingSelectionStatus,
    selectStaleShippingSelection,
} from '@/store/checkoutStore';
import type { ShippingAddress } from '@/types/checkout';
import { useCurrency } from '@/hooks';

interface ShippingStepProps {
    onContinue: () => void;
}

/**
 * Input Field Component
 */
interface InputFieldProps {
    label: string;
    name: keyof ShippingAddress;
    value: string;
    onChange: (value: string) => void;
    onBlur: () => void;
    error?: string;
    placeholder?: string;
    type?: string;
    required?: boolean;
    multiline?: boolean;
}

const InputField: React.FC<InputFieldProps> = ({
    label,
    name,
    value,
    onChange,
    onBlur,
    error,
    placeholder,
    type = 'text',
    required = false,
    multiline = false,
}) => {
    const inputId = `checkout-${name}`;
    const errorId = `${inputId}-error`;
    const hasError = !!error;

    const baseClasses = `
        w-full px-4 py-3
        bg-background
        border rounded-lg
        text-text placeholder:text-muted/50
        transition-colors duration-200
        focus:outline-none focus:ring-2 focus:ring-primary/50
        ${hasError
            ? 'border-red-300 focus:border-red-500 focus:ring-red-500/30'
            : 'border-muted/20 focus:border-primary'
        }
    `;

    return (
        <div className="space-y-1.5">
            <label htmlFor={inputId} className="block text-sm font-medium text-text">
                {label}
                {required && <span className="text-red-500 ml-0.5" aria-hidden="true">*</span>}
            </label>

            {multiline ? (
                <textarea
                    id={inputId}
                    name={name}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onBlur={onBlur}
                    placeholder={placeholder}
                    rows={3}
                    className={baseClasses}
                    aria-invalid={hasError}
                    aria-describedby={hasError ? errorId : undefined}
                    required={required}
                />
            ) : (
                <input
                    id={inputId}
                    type={type}
                    name={name}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onBlur={onBlur}
                    placeholder={placeholder}
                    className={baseClasses}
                    aria-invalid={hasError}
                    aria-describedby={hasError ? errorId : undefined}
                    required={required}
                />
            )}

            {hasError && (
                <p id={errorId} className="text-sm text-red-500 flex items-center gap-1" role="alert">
                    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {error}
                </p>
            )}
        </div>
    );
};

/**
 * Shipping Method Card
 */
interface ShippingMethodCardProps {
    id: string;
    name: string;
    description?: string;
    price: number;
    formatPrice: (amount: number) => string;
    estimatedDays?: number;
    isSelected: boolean;
    onSelect: () => void;
}

const ShippingMethodCard: React.FC<ShippingMethodCardProps> = ({
    id,
    name,
    description,
    price,
    formatPrice,
    estimatedDays,
    isSelected,
    onSelect,
}) => {
    return (
        <label
            className={`
                flex items-start gap-3 p-4
                border rounded-lg cursor-pointer
                transition-all duration-200
                ${isSelected
                    ? 'border-primary bg-primary/5 ring-2 ring-primary/20'
                    : 'border-muted/20 hover:border-muted/40 hover:bg-muted/5'
                }
            `}
        >
            <input
                type="radio"
                name="shippingMethod"
                value={id}
                checked={isSelected}
                onChange={onSelect}
                className="mt-1 w-4 h-4 text-primary border-muted/30 focus:ring-primary"
            />

            <div className="flex-1">
                <div className="flex items-center justify-between">
                    <span className="font-medium text-text">{name}</span>
                    <span className="font-semibold text-text">
                        {price > 0 ? formatPrice(price) : 'Free'}
                    </span>
                </div>

                {description && (
                    <p className="text-sm text-muted mt-0.5">{description}</p>
                )}

                {estimatedDays && (
                    <p className="text-xs text-muted mt-1">
                        Estimated delivery: {estimatedDays} day{estimatedDays !== 1 ? 's' : ''}
                    </p>
                )}
            </div>
        </label>
    );
};

/**
 * Shipping Step Component
 */
export const ShippingStep: React.FC<ShippingStepProps> = ({ onContinue }) => {
    const { t } = useTranslation();
    
    // Currency formatting from store config
    const { formatPrice } = useCurrency();

    // Store state
    const address = useCheckoutStore((state) => state.address);
    const shippingMethods = useCheckoutStore(selectShippingMethods);
    const selectedMethodId = useCheckoutStore(selectShippingMethodId);
    const isLoadingMethods = useCheckoutStore(selectShippingMethodsLoading);
    const shippingMethodsError = useCheckoutStore(selectShippingMethodsError);
    const shippingSelectionStatus = useCheckoutStore(selectShippingSelectionStatus);
    const staleShippingSelection = useCheckoutStore(selectStaleShippingSelection);

    // Store actions
    const setAddressField = useCheckoutStore((state) => state.setAddressField);
    const touchField = useCheckoutStore((state) => state.touchField);
    const setShippingMethod = useCheckoutStore((state) => state.setShippingMethod);
    const fetchShippingMethods = useCheckoutStore((state) => state.fetchShippingMethods);
    const getFieldError = useCheckoutStore((state) => state.getFieldError);
    const isStepValid = useCheckoutStore((state) => state.isStepValid);
    const setShowAllErrors = useCheckoutStore((state) => state.setShowAllErrors);

    // Fetch shipping methods on mount
    useEffect(() => {
        if (shippingMethods.length === 0) {
            fetchShippingMethods();
        }
    }, [shippingMethods.length, fetchShippingMethods]);

    // Handle continue button
    const handleContinue = () => {
        if (isStepValid('shipping')) {
            onContinue();
        } else {
            setShowAllErrors(true);
        }
    };

    const canContinue = isStepValid('shipping');

    return (
        <div className="space-y-8">
            {/* Shipping Address */}
            <section>
                <h2 className="text-lg font-semibold text-text mb-4">Shipping Address</h2>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <InputField
                        label="Full Name"
                        name="fullName"
                        value={address.fullName}
                        onChange={(value) => setAddressField('fullName', value)}
                        onBlur={() => touchField('fullName')}
                        error={getFieldError('fullName')}
                        placeholder="Enter your full name"
                        required
                    />

                    <InputField
                        label="Phone Number"
                        name="phone"
                        type="tel"
                        value={address.phone}
                        onChange={(value) => setAddressField('phone', value)}
                        onBlur={() => touchField('phone')}
                        error={getFieldError('phone')}
                        placeholder="e.g. 01022174803"
                        required
                    />
                    {/* Phone number hint */}
                    <p className="text-xs text-muted -mt-2 mb-2">
                        Enter your local number. Country code is optional.
                    </p>

                    <InputField
                        label="City"
                        name="city"
                        value={address.city}
                        onChange={(value) => setAddressField('city', value)}
                        onBlur={() => touchField('city')}
                        error={getFieldError('city')}
                        placeholder="Enter city"
                        required
                    />

                    <InputField
                        label="Area / District"
                        name="area"
                        value={address.area}
                        onChange={(value) => setAddressField('area', value)}
                        onBlur={() => touchField('area')}
                        error={getFieldError('area')}
                        placeholder="Enter area or district"
                        required
                    />

                    <div className="md:col-span-2">
                        <InputField
                            label="Street Address"
                            name="addressLine"
                            value={address.addressLine}
                            onChange={(value) => setAddressField('addressLine', value)}
                            onBlur={() => touchField('addressLine')}
                            error={getFieldError('addressLine')}
                            placeholder="Enter your full street address"
                            multiline
                            required
                        />
                    </div>

                    <div className="md:col-span-2">
                        <InputField
                            label="Delivery Notes (Optional)"
                            name="notes"
                            value={address.notes || ''}
                            onChange={(value) => setAddressField('notes', value)}
                            onBlur={() => touchField('notes')}
                            placeholder="Any special instructions for delivery"
                            multiline
                        />
                    </div>
                </div>
            </section>

            {/* Shipping Method */}
            <section>
                <h2 className="text-lg font-semibold text-text mb-4">Shipping Method</h2>

                {isLoadingMethods ? (
                    <div className="space-y-3">
                        {[1, 2].map((i) => (
                            <div key={i} className="h-20 bg-muted/10 rounded-lg skeleton" />
                        ))}
                    </div>
                ) : shippingMethodsError ? (
                    <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                        <p>{shippingMethodsError}</p>
                        <p className="mt-1 text-red-600">
                            We could not load the latest shipping options. Please retry before continuing.
                        </p>
                        <button
                            type="button"
                            onClick={() => void fetchShippingMethods()}
                            className="mt-3 inline-flex items-center rounded-lg border border-red-300 px-3 py-2 font-medium text-red-700 hover:bg-red-100"
                        >
                            Retry shipping options
                        </button>
                    </div>
                ) : shippingMethods.length === 0 ? (
                    <div className="p-4 bg-amber-50 border border-amber-200 rounded-lg text-amber-800 text-sm">
                        No shipping methods available. Please try again later.
                    </div>
                ) : (
                    <div className="space-y-3">
                        {shippingSelectionStatus === 'stale' && staleShippingSelection && (
                            <div 
                                className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800"
                                role="status"
                                aria-live="polite"
                                aria-atomic="true"
                            >
                                <p className="font-medium">{t(staleShippingSelection.message, { defaultValue: 'Your previous selection is no longer available' })}</p>
                                <p className="mt-1 text-amber-700">
                                    {t('checkout.previousSelection')}: {staleShippingSelection.label}
                                </p>
                            </div>
                        )}
                        {shippingMethods.map((method) => (
                            <ShippingMethodCard
                                key={method.id}
                                id={method.id}
                                name={method.name}
                                description={method.description}
                                price={method.price}
                                formatPrice={formatPrice}
                                estimatedDays={method.estimatedDays}
                                isSelected={selectedMethodId === method.id}
                                onSelect={() => setShippingMethod(method.id)}
                            />
                        ))}
                    </div>
                )}
            </section>

            {/* Continue Button */}
            <div className="pt-4">
                <button
                    onClick={handleContinue}
                    disabled={!canContinue}
                    className={`
                        w-full py-3.5 px-6
                        rounded-xl font-medium
                        transition-all duration-200
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        ${canContinue
                            ? 'bg-primary text-white hover:bg-primary/90 shadow-md hover:shadow-lg'
                            : 'bg-muted/20 text-muted cursor-not-allowed'
                        }
                    `}
                    aria-disabled={!canContinue}
                >
                    Continue to Payment
                </button>
            </div>
        </div>
    );
};
