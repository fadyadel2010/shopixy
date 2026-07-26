/**
 * Checkout Types
 * 
 * Type definitions for checkout flow.
 * Matches backend Checkout API contracts.
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import type { CartDto } from './cart';

// ============================================================================
// Checkout Steps
// ============================================================================

export type CheckoutStep = 'shipping' | 'payment' | 'review';

// ============================================================================
// Address Types
// ============================================================================

/**
 * Frontend shipping address form structure
 */
export interface ShippingAddress {
    fullName: string;
    phone: string;
    city: string;
    area: string;
    addressLine: string;
    country?: string;
    geography?: GeographicAddressCandidate;
    building?: string;
    floor?: string;
    apartment?: string;
    landmark?: string;
    notes?: string;
}

export interface GeographicAddressCandidate {
    countryPlaceId?: string | null;
    administrativeAreaPlaceId?: string | null;
    localityPlaceId?: string | null;
    districtPlaceId?: string | null;
    zonePlaceId?: string | null;
}

/**
 * Backend-compatible address DTO
 * Maps frontend ShippingAddress to backend AddressDto
 */
export interface AddressDto {
    name: string;       // Maps from fullName
    phone: string;
    email?: string;
    street: string;     // Maps from addressLine
    street2?: string;
    city: string;
    state?: string;     // Maps from area
    zipCode?: string;
    country: string;    // Derived from address or storefront config
    geography?: GeographicAddressCandidate;
    building?: string;
    floor?: string;
    apartment?: string;
    landmark?: string;
}

// ============================================================================
// Shipping Method Types
// ============================================================================

export interface ShippingMethod {
    tenantId: string;
    id: string;
    name: string;
    description?: string;
    price: number;
    currency: string;
    estimatedDays?: number;
}

// ============================================================================
// Payment Types
// ============================================================================

/**
 * Legacy payment method type (for backward compatibility)
 * @deprecated Use PaymentMethodDto.code instead
 */
export type PaymentMethodLegacy = 'cod' | 'online';

/**
 * Payment method type (derived from code)
 */
export type PaymentMethod = 'COD' | 'STRIPE' | 'PAYMOB' | 'FAWRY' | string;

/**
 * Payment method DTO from Storefront API
 * 
 * PHASE 10.4.D2: Payment Methods UI
 * 
 * ARCHITECTURE:
 * - Fetched from GET /api/storefront/payment-methods
 * - Only enabled methods are returned
 * - NO secrets are included
 * - POS Admin controls all configuration
 */
export interface PaymentMethodDto {
    /** Unique identifier */
    id: string;

    /** Payment method code: COD, STRIPE, PAYMOB */
    code: string;

    /** Display name (English) */
    displayName: string;

    /** Display name (Arabic) */
    displayNameAr?: string;

    /** Description (English) */
    description?: string;

    /** Description (Arabic) */
    descriptionAr?: string;

    /** Whether this method supports online payments */
    supportsOnline: boolean;

    /** Whether this method supports offline payments (COD) */
    supportsOffline: boolean;

    /** Icon identifier */
    icon?: string;

    /** Minimum order amount for this method */
    minOrderAmount?: number;

    /** Maximum order amount for this method */
    maxOrderAmount?: number;

    /** Additional fee for using this method */
    fee: number;

    /** Fee type: "fixed" or "percentage" */
    feeType: 'fixed' | 'percentage';

    /** Display order */
    sortOrder: number;

    /** Whether this method can currently be used by customers */
    isAvailable: boolean;

    /** Machine-readable unavailability reason */
    unavailableReasonCode?: string | null;
}

// ============================================================================
// Checkout Request/Response
// ============================================================================

export interface CheckoutRequest {
    cartId: string;
    shippingAddress: AddressDto;
    billingAddress?: AddressDto;
    shippingMethodId: string;
    paymentMethod: PaymentMethod;
    couponCode?: string;
    customerNotes?: string;
    expectedPricing: CheckoutExpectedPricing;
    // Manual Wallet Fields
    walletType?: string;
    walletPhoneNumber?: string;
    proofFileKey?: string;
}

export interface CheckoutPricingPreviewRequest {
    cartId: string;
    shippingMethodId?: string;
    paymentMethod?: string;
    shippingCountry?: string;
    shippingGeography?: GeographicAddressCandidate;
}

export interface RecoveredCheckoutCartItem {
    variantId?: string | null;
    productName: string;
    variantName?: string | null;
    quantity: number;
    price: number;
    imageUrl?: string | null;
}

export interface RecoveredCheckoutAddress {
    fullName: string;
    phone: string;
    email?: string | null;
    city: string;
    area: string;
    addressLine: string;
    country?: string | null;
}

export interface RecoveredCheckoutResponse {
    sessionId: string;
    guestSessionId?: string | null;
    checkoutToken?: string | null;
    cartId: string;
    status: string;
    cart: RecoveredCheckoutCartItem[];
    address: RecoveredCheckoutAddress;
    shippingMethod?: {
        id: string;
    } | null;
    paymentMethod?: {
        code: string;
    } | null;
    notes?: string | null;
    cartTotal?: number | null;
    currency?: string | null;
    shippingMethodId?: string | null;
    paymentMethodCode?: string | null;
    couponCode?: string | null;
    couponDiscount?: number | null;
    couponType?: 'percentage' | 'fixed' | string | null;
    couponValue?: number | null;
    promotionId?: string | null;
    cartPromotionDiscount?: number | null;
    promotionName?: string | null;
    promotionDescription?: string | null;
    promotionDiscountType?: string | null;
    promotionDiscountValue?: number | null;
    promotionBadge?: string | null;
    cartWasRevalidated?: boolean;
    cartChangedDuringRecovery?: boolean;
    requiresReview?: boolean;
    recoveryMessage?: string | null;
    revalidatedAtUtc?: string | null;
}

export interface RecoveredCartResponse {
    tenantId: string;
    cartId: string;
    sessionId?: string | null;
    customerId?: string | null;
    cartSnapshot?: CartDto | null;
    message: string;
}

export interface ManualWalletTypeOption {
    code: string;
    name?: string;
    nameAr?: string;
    icon?: string;
}

export interface ManualWalletUploadConfig {
    walletNumber: string;
    walletTypes?: ManualWalletTypeOption[];
    allowedMimeTypes: string[];
    allowedExtensions: string[];
    maxFileBytes: number;
}

export interface CheckoutPricingBreakdown {
    subtotal: number;
    shipping: number;
    tax: number;
    discount: number;
    total: number;
    couponDiscount: number;
    promotionDiscount: number;
    cartPromotionDiscount: number;
    paymentFee: number;
    taxRate: number;
    taxSource: string;
    taxCountryCode?: string | null;
    pricesRefreshed: boolean;
}

export interface CheckoutExpectedPricing {
    subtotal: number;
    shipping: number;
    tax: number;
    discount: number;
    total: number;
    couponDiscount: number;
    promotionDiscount: number;
    cartPromotionDiscount: number;
    paymentFee: number;
}

export interface CheckoutResponse {
    orderId: string;
    orderNumber: string;
    status: string;
    cartId: string;
    paymentIntentId?: string | null;
    total: number;
    totalAmount: number; // Backend uses TotalAmount
    currency: string;
    checkoutToken?: string | null;
    trackingToken?: string | null;
    promotionCalculationFailed?: boolean;
    /** @deprecated Use promotionWarningCode and promotionWarningParams for localization support */
    promotionWarningMessage?: string | null;
    promotionWarningCode?: string | null;
    promotionWarningParams?: Record<string, string> | null;
    paymentStatus: 'pending' | 'completed' | 'failed';
    createdAt: string;
    paymentReference?: string;
    redirectUrl?: string;
    stripeClientSecret?: string;
    stripePublishableKey?: string;
    // Paymob fields (Phase 10.4.D5)
    paymobIframeUrl?: string;
    paymobPaymentKey?: string;
    // Fawry
    referenceCode?: string;
    expiresAtUtc?: string;
    // Manual Wallet (Phase P3)
    isManualWallet?: boolean;
    merchantWalletNumber?: string;
    walletType?: string;
    walletInstructions?: string;
    totals?: any; // Add totals if needed, or map properly
}


// ============================================================================
// COD Confirmation
// ============================================================================

export interface CodConfirmResponse {
    success: boolean;
    orderId: string;
    message?: string;
}

// ============================================================================
// Validation
// ============================================================================

export interface AddressValidationErrors {
    fullName?: string;
    phone?: string;
    city?: string;
    area?: string;
    addressLine?: string;
}

// ============================================================================
// Order Result
// ============================================================================

export interface OrderResult {
    orderId: string;
    orderNumber: string;
    paymentIntentId?: string | null;
    total: number;
    currency: string;
    checkoutToken?: string | null;
    trackingToken?: string | null;
    promotionCalculationFailed?: boolean;
    /** @deprecated Use promotionWarningCode and promotionWarningParams for localization support */
    promotionWarningMessage?: string | null;
    promotionWarningCode?: string | null;
    promotionWarningParams?: Record<string, string> | null;
    paymentMethod: PaymentMethod;
    stripeClientSecret?: string;
    stripePublishableKey?: string;
    // Paymob payment fields (Phase 10.4.D5)
    paymobIframeUrl?: string;
    paymobPaymentKey?: string;
    // Fawry
    referenceCode?: string;
    expiresAtUtc?: string;
    // Manual Wallet (Phase P3)
    isManualWallet?: boolean;
    merchantWalletNumber?: string;
    walletType?: string;
    walletInstructions?: string;
    // Map backend response fields
    totalAmount?: number;
    redirectUrl?: string;
    paymentReference?: string;
}

// ============================================================================
// Phone OTP Verification (WhatsApp)
// ============================================================================

/**
 * Phone verification status response
 * Used to check if OTP is required before checkout
 */
export interface PhoneVerificationStatus {
    /** True if phone verification is required before checkout */
    isRequired: boolean;
    /** True if the phone has already been verified */
    isVerified: boolean;
    /** The phone number that needs verification (as entered) */
    phone?: string;
    /** The phone number formatted in E.164 format (e.g., +201022174803) */
    formattedPhone?: string;
    /** Message for the customer */
    message: string;
}

/**
 * Phone OTP request response
 */
export interface PhoneOtpResult {
    success: boolean;
    error?: string;
    message?: string;
    expiresInSeconds?: number;
    retryAfterSeconds?: number;
}

/**
 * Phone OTP verification request
 */
export interface VerifyPhoneOtpRequest {
    phone: string;
    otpCode: string;
}
