/**
 * Checkout Store (Zustand)
 * 
 * Manages checkout state including:
 * - Step navigation
 * - Shipping address
 * - Shipping method selection
 * - Payment method selection
 * - Form validation
 * - Checkout submission
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import { create } from 'zustand';
import { createJSONStorage, persist, type StateStorage } from 'zustand/middleware';
import { httpClient, normalizeError, isRetryableError } from '@/services/api';
import { useCartStore, waitForCartMerge } from './cartStore';
import { useAuthStore, selectIsAuthenticated, selectAuthStatus } from '@/auth/store/authStore';
import { useStoreConfigStore } from './storeConfigStore';
import { resolveCountryCode } from '@/utils/country';
import { persistActiveCheckoutOrderContext } from '@/features/checkout/activeCheckoutOrderContext';
import { requireStorefrontPaymentSessionAuthority } from '@/features/checkout/paymentSessionAuthority';
import { emitCheckoutSelectionInvalidated } from '@/features/checkout/checkoutSelectionTelemetry';
import { getRequestHeaders } from '@/bootstrap/requestContext';
import { tryGetTenantScopedStorageKey } from '@/bootstrap/tenantScopedStorage';
import type {
    CheckoutStep,
    ShippingAddress,
    PaymentMethod,
    PaymentMethodDto,
    ShippingMethod,
    CheckoutRequest,
    CheckoutResponse,
    CheckoutExpectedPricing,
    CheckoutPricingBreakdown,
    CheckoutPricingPreviewRequest,
    AddressValidationErrors,
    ManualWalletUploadConfig,
    ManualWalletTypeOption,
    OrderResult,
} from '@/types/checkout';

// ============================================================================
// Constants
// ============================================================================

const STORAGE_KEY = 'checkout-state';
const SENSITIVE_STORAGE_KEY = 'checkout-sensitive-session';
const AUTOSAVE_DEBOUNCE_MS = 1500;
const AUTOSAVE_MAX_RETRIES = 3;
const AUTOSAVE_RETRY_BASE_DELAY_MS = 750;

// Checkout API endpoints
const CHECKOUT_ENDPOINTS = {
    CHECKOUT: '/api/storefront/checkout',
    PRICING: '/api/storefront/checkout/pricing',
    SHIPPING_METHODS: '/api/storefront/shipping-methods',
    PAYMENT_METHODS: '/api/storefront/payment-methods',
    // Phase: Abandoned Checkout — autosave session endpoints
    CHECKOUT_SESSION: '/api/storefront/checkout-session',
    CHECKOUT_SESSION_UPDATE: (id: string) => `/api/storefront/checkout-session/${id}`,
} as const;

export type CheckoutAutosaveStatus = 'idle' | 'unsaved' | 'saving' | 'saved' | 'error';
export type CheckoutSelectionStatus = 'none' | 'valid' | 'stale';

interface StaleCheckoutSelection {
    value: string;
    label: string;
    message: string;
}

interface CheckoutSessionResponseDto {
    sessionId?: string | null;
    manualWalletUploadToken?: string | null;
}

let checkoutAutosaveTimer: ReturnType<typeof setTimeout> | null = null;
let checkoutAutosaveAbortController: AbortController | null = null;
let checkoutAutosaveRequestSequence = 0;
let checkoutSessionInitPromise: Promise<string | null> | null = null;
let checkoutSessionInitAbortController: AbortController | null = null;
let pricingPreviewAbortController: AbortController | null = null;
let pricingPreviewRequestSequence = 0;
const STALE_SELECTION_ERROR_KEY = 'checkout.staleSelection';

function clearCheckoutAutosaveTimer(): void {
    if (checkoutAutosaveTimer) {
        clearTimeout(checkoutAutosaveTimer);
        checkoutAutosaveTimer = null;
    }
}

function cancelCheckoutAutosaveRequest(): void {
    if (checkoutAutosaveAbortController) {
        checkoutAutosaveAbortController.abort();
        checkoutAutosaveAbortController = null;
    }
}

function cancelCheckoutSessionInit(): void {
    if (checkoutSessionInitAbortController) {
        checkoutSessionInitAbortController.abort();
        checkoutSessionInitAbortController = null;
    }
    checkoutSessionInitPromise = null;
}

function cancelPricingPreviewRequest(): void {
    if (pricingPreviewAbortController) {
        pricingPreviewAbortController.abort();
        pricingPreviewAbortController = null;
    }
}

function buildExpectedPricing(pricing: CheckoutPricingBreakdown): CheckoutExpectedPricing {
    return {
        subtotal: pricing.subtotal,
        shipping: pricing.shipping,
        tax: pricing.tax,
        discount: pricing.discount,
        total: pricing.total,
        couponDiscount: pricing.couponDiscount,
        promotionDiscount: pricing.promotionDiscount,
        cartPromotionDiscount: pricing.cartPromotionDiscount,
        paymentFee: pricing.paymentFee,
    };
}

function isAutosaveAbortError(error: unknown): boolean {
    if (error instanceof DOMException && error.name === 'AbortError') {
        return true;
    }

    if (typeof error === 'object' && error !== null) {
        const candidate = error as { code?: string; name?: string };
        return candidate.code === 'ERR_CANCELED' || candidate.name === 'CanceledError';
    }

    return false;
}

function waitForAutosaveRetry(delayMs: number, signal: AbortSignal): Promise<void> {
    return new Promise((resolve, reject) => {
        const timeoutId = setTimeout(() => {
            cleanup();
            resolve();
        }, delayMs);

        const handleAbort = () => {
            cleanup();
            reject(new DOMException('Autosave retry aborted', 'AbortError'));
        };

        const cleanup = () => {
            clearTimeout(timeoutId);
            signal.removeEventListener('abort', handleAbort);
        };

        signal.addEventListener('abort', handleAbort, { once: true });
    });
}

function sortKeysRecursively(value: unknown): unknown {
    if (Array.isArray(value)) {
        return value.map((item) => sortKeysRecursively(item));
    }

    if (value && typeof value === 'object') {
        const record = value as Record<string, unknown>;
        return Object.keys(record)
            .sort()
            .reduce<Record<string, unknown>>((sorted, key) => {
                sorted[key] = sortKeysRecursively(record[key]);
                return sorted;
            }, {});
    }

    return value;
}

/*
 * ASCII-only invariant for serialized checkout snapshots:
 * Object.keys().sort() uses Unicode lexicographic ordering, while the server
 * canonicalizes with StringComparer.Ordinal. Those orderings are equivalent for
 * ASCII property names, which is why the checkout snapshot schema must remain ASCII-only.
 * This is intentional, not an oversight. If we ever add a non-ASCII property name here,
 * add an explicit client/server sort-parity test before shipping the change.
 */
function serializeWithSortedKeys(value: unknown): string {
    return JSON.stringify(sortKeysRecursively(value));
}

function getCheckoutItemsSnapshot() {
    const cart = useCartStore.getState().cart;

    return cart?.items?.map((item) => ({
        variantId: item.variantId || null,
        productName: item.productName || '',
        variantName: item.variantAttributes?.map((attribute) => attribute.value).join(' / ') || null,
        quantity: item.quantity,
        price: item.unitPrice || 0,
        imageUrl: item.imageUrl || null,
    })) || [];
}

function getDeviceType(): string {
    const userAgent = navigator.userAgent || '';

    if (/Mobi|Android|iPhone|iPod/i.test(userAgent)) {
        return 'Mobile';
    }

    if (/iPad|Tablet/i.test(userAgent)) {
        return 'Tablet';
    }

    return 'Desktop';
}

function getTrafficSource(): string {
    const params = new URLSearchParams(window.location.search);
    const utmSource = params.get('utm_source');
    if (utmSource) {
        return utmSource;
    }

    if (!document.referrer) {
        return 'Direct';
    }

    try {
        const referrerUrl = new URL(document.referrer);
        if (referrerUrl.hostname !== window.location.hostname) {
            return referrerUrl.hostname.replace('www.', '');
        }
    } catch {
        // Ignore malformed referrers and fall back to direct traffic.
    }

    return 'Direct';
}

function buildCheckoutAutosaveErrorMessage(
    error: unknown,
    fallbackMessage = "checkout.autosaveFailed"
): string {
    if (isAutosaveAbortError(error)) {
        return fallbackMessage;
    }

    if (typeof error === 'object' && error !== null && 'response' in error) {
        const normalized = normalizeError(error as any);
        return normalized.message || fallbackMessage;
    }

    if (error instanceof Error && error.message) {
        return error.message;
    }

    return fallbackMessage;
}

async function ensureCheckoutSessionInitialized(): Promise<string | null> {
    const existingSessionId = useCheckoutStore.getState().checkoutSessionId;
    const existingUploadToken = useCheckoutStore.getState().manualWalletUploadToken;
    if (existingSessionId && existingUploadToken) {
        return existingSessionId;
    }

    if (checkoutSessionInitPromise) {
        return checkoutSessionInitPromise;
    }

    checkoutSessionInitPromise = (async () => {
        const cart = useCartStore.getState().cart;
        if (!cart?.cartId) {
            return null;
        }

        // Capture the cartId at the start to validate response is still relevant
        const capturedCartId = cart.cartId;

        // Create abort controller for this session init request
        checkoutSessionInitAbortController?.abort();
        const controller = new AbortController();
        checkoutSessionInitAbortController = controller;

        try {
            const response = await httpClient.post<CheckoutSessionResponseDto>(
                CHECKOUT_ENDPOINTS.CHECKOUT_SESSION,
                {
                    cartId: cart.cartId,
                    itemsSnapshotJson: serializeWithSortedKeys(getCheckoutItemsSnapshot()),
                    cartTotal: cart.grandTotal || cart.subtotal || 0,
                    currency: cart.currency || undefined,
                    deviceType: getDeviceType(),
                    source: getTrafficSource(),
                },
                { signal: controller.signal }
            );

            // Verify response is still relevant before updating state
            const currentCartId = useCartStore.getState().cart?.cartId;
            if (currentCartId !== capturedCartId || controller.signal.aborted) {
                console.log('[CheckoutSession] Session init response is stale, ignoring');
                return null;
            }

            const sessionId = response.data?.sessionId ?? null;
            const manualWalletUploadToken = response.data?.manualWalletUploadToken ?? null;
            if (!sessionId) {
                throw new Error('Checkout autosave did not return a session id.');
            }

            useCheckoutStore.setState({
                checkoutSessionId: sessionId,
                manualWalletUploadToken,
            });
            console.log('[CheckoutSession] Session initialized:', sessionId);
            return sessionId;
        } catch (error) {
            // Don't update state if request was aborted
            if (isAutosaveAbortError(error)) {
                console.log('[CheckoutSession] Session init aborted');
                return null;
            }

            console.error('[CheckoutSession] Failed to initialize checkout session:', error);
            useCheckoutStore.setState({
                autosaveStatus: 'error',
                autosaveError: buildCheckoutAutosaveErrorMessage(
                    error,
                    "checkout.autosaveInitFailed"
                ),
            });
            return null;
        } finally {
            if (checkoutSessionInitAbortController === controller) {
                checkoutSessionInitAbortController = null;
            }
            checkoutSessionInitPromise = null;
        }
    })();

    return checkoutSessionInitPromise;
}

async function performCheckoutAutosave(): Promise<boolean> {
    clearCheckoutAutosaveTimer();

    const cart = useCartStore.getState().cart;
    if (!cart?.cartId) {
        useCheckoutStore.setState({
            autosaveStatus: 'idle',
            autosaveError: null,
            lastAutosavedAt: null,
        });
        return false;
    }

    const sessionId = await ensureCheckoutSessionInitialized();
    if (!sessionId) {
        return false;
    }

    cancelCheckoutAutosaveRequest();
    const controller = new AbortController();
    checkoutAutosaveAbortController = controller;
    const requestSequence = ++checkoutAutosaveRequestSequence;

    useCheckoutStore.setState({
        autosaveStatus: 'saving',
        autosaveError: null,
    });

    const state = useCheckoutStore.getState();
    const payload = {
        customerName: state.address.fullName || undefined,
        phone: state.address.phone || undefined,
        city: state.address.city || undefined,
        area: state.address.area || undefined,
        address: state.address.addressLine || undefined,
        country: state.address.country || undefined,
        notes: state.address.notes || undefined,
        shippingMethodId: state.shippingMethodId || undefined,
        paymentMethodCode: state.selectedPaymentMethodCode || undefined,
        walletType: state.manualWalletType || undefined,
        walletPhoneNumber: state.manualWalletPhoneNumber || undefined,
        itemsSnapshotJson: serializeWithSortedKeys(getCheckoutItemsSnapshot()),
        cartTotal: cart.grandTotal || cart.subtotal || undefined,
        currency: cart.currency || undefined,
    };

    let lastError: unknown = null;

    try {
        for (let attempt = 0; attempt <= AUTOSAVE_MAX_RETRIES; attempt += 1) {
            try {
                await httpClient.patch(
                    CHECKOUT_ENDPOINTS.CHECKOUT_SESSION_UPDATE(sessionId),
                    payload,
                    { signal: controller.signal }
                );

                if (controller.signal.aborted || requestSequence !== checkoutAutosaveRequestSequence) {
                    return false;
                }

                useCheckoutStore.setState({
                    autosaveStatus: 'saved',
                    autosaveError: null,
                    lastAutosavedAt: new Date().toISOString(),
                });
                return true;
            } catch (error) {
                if (isAutosaveAbortError(error)) {
                    return false;
                }

                lastError = error;

                if (attempt >= AUTOSAVE_MAX_RETRIES || !isRetryableError(error)) {
                    break;
                }

                try {
                    await waitForAutosaveRetry(
                        AUTOSAVE_RETRY_BASE_DELAY_MS * Math.pow(2, attempt),
                        controller.signal
                    );
                } catch (retryError) {
                    if (isAutosaveAbortError(retryError)) {
                        return false;
                    }

                    lastError = retryError;
                    break;
                }
            }
        }

        if (!controller.signal.aborted && requestSequence === checkoutAutosaveRequestSequence) {
            useCheckoutStore.setState({
                autosaveStatus: 'error',
                autosaveError: buildCheckoutAutosaveErrorMessage(lastError),
            });
        }

        return false;
    } finally {
        if (checkoutAutosaveAbortController === controller) {
            checkoutAutosaveAbortController = null;
        }
    }
}

function scheduleCheckoutAutosave(): void {
    cancelCheckoutAutosaveRequest();
    clearCheckoutAutosaveTimer();

    useCheckoutStore.setState({
        autosaveStatus: 'unsaved',
        autosaveError: null,
    });

    checkoutAutosaveTimer = setTimeout(() => {
        void performCheckoutAutosave();
    }, AUTOSAVE_DEBOUNCE_MS);
}

// ============================================================================
// Validation
// ============================================================================

const PHONE_REGEX = /^[\d\s\-+()]{8,20}$/;
const PAYMENT_METHOD_CODE_REGEX = /^[A-Z0-9_]{2,50}$/;
const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const SHIPPING_CURRENCY_REGEX = /^[A-Z]{3}$/;

export interface ManualWalletFieldError {
    code: string;
}

export interface ManualWalletValidationErrors {
    walletType?: ManualWalletFieldError;
    walletPhoneNumber?: ManualWalletFieldError;
    proofFileKey?: ManualWalletFieldError;
}

export function validateManualWalletFields(values: {
    walletType?: string | null;
    walletPhoneNumber?: string | null;
    proofFileKey?: string | null;
    walletTypes?: ManualWalletTypeOption[] | null;
    requireKnownWalletType?: boolean;
}): ManualWalletValidationErrors {
    const errors: ManualWalletValidationErrors = {};
    const allowedWalletTypeCodes = Array.isArray(values.walletTypes)
        ? values.walletTypes
            .map((walletType) => walletType.code?.trim())
            .filter((code): code is string => Boolean(code))
        : [];

    if (!values.walletType?.trim()) {
        errors.walletType = {
            code: 'MANUAL_WALLET_TYPE_REQUIRED',
        };
    } else if (
        values.requireKnownWalletType === true &&
        (
            allowedWalletTypeCodes.length === 0 ||
            !allowedWalletTypeCodes.some((walletType) => walletType.toLowerCase() === values.walletType?.trim().toLowerCase())
        )
    ) {
        errors.walletType = {
            code: 'MANUAL_WALLET_TYPE_INVALID',
        };
    }

    if (!values.walletPhoneNumber?.trim()) {
        errors.walletPhoneNumber = {
            code: 'MANUAL_WALLET_PHONE_REQUIRED',
        };
    } else if (!PHONE_REGEX.test(values.walletPhoneNumber.trim())) {
        errors.walletPhoneNumber = {
            code: 'INVALID_MANUAL_WALLET_PHONE',
        };
    }

    if (!values.proofFileKey?.trim()) {
        errors.proofFileKey = {
            code: 'MANUAL_WALLET_PROOF_REQUIRED',
        };
    }

    return errors;
}

function getFirstManualWalletError(errors: ManualWalletValidationErrors): { code: string } {
    if (errors.walletType) {
        return errors.walletType;
    }

    if (errors.walletPhoneNumber) {
        return errors.walletPhoneNumber;
    }

    if (errors.proofFileKey) {
        return errors.proofFileKey;
    }

    return {
        code: 'MANUAL_WALLET_PROOF_REQUIRED',
    };
}

function isManualWalletSelectionValid(values: {
    walletType?: string | null;
    walletPhoneNumber?: string | null;
    proofFileKey?: string | null;
    walletTypes?: ManualWalletTypeOption[] | null;
}): boolean {
    return Object.keys(validateManualWalletFields({
        ...values,
        requireKnownWalletType: true,
    })).length === 0;
}

function isFiniteNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}

function isOptionalNonNegativeNumber(value: unknown): value is number | undefined {
    return value === undefined || value === null || (isFiniteNumber(value) && value >= 0);
}

function isOptionalString(value: unknown): value is string | undefined {
    return value === undefined || value === null || typeof value === 'string';
}

function requireShippingString(value: unknown, field: string): string {
    if (typeof value !== 'string') {
        throw new Error(`Shipping method response missing ${field}.`);
    }

    const normalized = value.trim();
    if (!normalized) {
        throw new Error(`Shipping method response missing ${field}.`);
    }

    return normalized;
}

function optionalShippingString(value: unknown): string | undefined {
    if (value === undefined || value === null) {
        return undefined;
    }

    if (typeof value !== 'string') {
        throw new Error('Shipping method response contained malformed optional text evidence.');
    }

    const normalized = value.trim();
    return normalized || undefined;
}

function requireShippingAmount(value: unknown, field: string): number {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
        throw new Error(`Shipping method response contained invalid ${field}.`);
    }

    return value;
}

function optionalEstimatedDays(value: unknown): number | undefined {
    if (value === undefined || value === null) {
        return undefined;
    }

    if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > 365) {
        throw new Error('Shipping method response contained invalid ETA evidence.');
    }

    return value;
}

function admitStorefrontShippingMethods(payload: unknown): ShippingMethod[] {
    if (!Array.isArray(payload)) {
        throw new Error('Shipping method response failed admission.');
    }

    const expectedTenantId = getRequestHeaders()['X-Tenant-Id'];
    if (!UUID_REGEX.test(expectedTenantId)) {
        throw new Error('Shipping method response could not be admitted without tenant scope.');
    }

    const seenIds = new Set<string>();

    return payload.map((method) => {
        if (!method || typeof method !== 'object') {
            throw new Error('Shipping method response contained malformed DTO evidence.');
        }

        const candidate = method as Record<string, unknown>;
        const tenantId = requireShippingString(candidate.tenantId, 'tenantId');
        const id = requireShippingString(candidate.id, 'id');
        const name = requireShippingString(candidate.name, 'name');
        const currency = requireShippingString(candidate.currency, 'currency').toUpperCase();

        if (!UUID_REGEX.test(tenantId) || tenantId.toLowerCase() !== expectedTenantId.toLowerCase()) {
            throw new Error('Shipping method response contained foreign tenant evidence.');
        }

        if (!UUID_REGEX.test(id)) {
            throw new Error('Shipping method response contained malformed identity evidence.');
        }

        if (seenIds.has(id)) {
            throw new Error('Shipping method response contained duplicate identity evidence.');
        }

        seenIds.add(id);

        if (!SHIPPING_CURRENCY_REGEX.test(currency)) {
            throw new Error('Shipping method response contained invalid currency evidence.');
        }

        return {
            tenantId,
            id,
            name,
            description: optionalShippingString(candidate.description),
            price: requireShippingAmount(candidate.price, 'price'),
            currency,
            estimatedDays: optionalEstimatedDays(candidate.estimatedDays),
        };
    });
}

function admitStorefrontPaymentMethods(payload: unknown): PaymentMethodDto[] {
    if (!Array.isArray(payload)) {
        throw new Error('Payment method response was not a list.');
    }

    const seenIds = new Set<string>();
    const seenCodes = new Set<string>();
    const admitted: PaymentMethodDto[] = [];

    for (const item of payload) {
        if (!item || typeof item !== 'object') {
            throw new Error('Payment method response contained a malformed item.');
        }

        const candidate = item as Partial<PaymentMethodDto>;
        const id = typeof candidate.id === 'string' ? candidate.id.trim() : '';
        const code = typeof candidate.code === 'string' ? candidate.code.trim().toUpperCase() : '';
        const displayName = typeof candidate.displayName === 'string' ? candidate.displayName.trim() : '';
        const feeType = candidate.feeType;
        const fee = candidate.fee;
        const sortOrder = candidate.sortOrder;

        if (
            !UUID_REGEX.test(id) ||
            !PAYMENT_METHOD_CODE_REGEX.test(code) ||
            !displayName ||
            typeof candidate.supportsOnline !== 'boolean' ||
            typeof candidate.supportsOffline !== 'boolean' ||
            typeof candidate.isAvailable !== 'boolean' ||
            (!candidate.supportsOnline && !candidate.supportsOffline) ||
            !isFiniteNumber(fee) ||
            fee < 0 ||
            (feeType !== 'fixed' && feeType !== 'percentage') ||
            !Number.isInteger(sortOrder) ||
            typeof sortOrder !== 'number' ||
            sortOrder < 0 ||
            !isOptionalNonNegativeNumber(candidate.minOrderAmount) ||
            !isOptionalNonNegativeNumber(candidate.maxOrderAmount) ||
            !isOptionalString(candidate.displayNameAr) ||
            !isOptionalString(candidate.description) ||
            !isOptionalString(candidate.descriptionAr) ||
            !isOptionalString(candidate.icon) ||
            !isOptionalString(candidate.unavailableReasonCode)
        ) {
            throw new Error('Payment method response failed admission.');
        }

        if (
            candidate.minOrderAmount != null &&
            candidate.maxOrderAmount != null &&
            candidate.minOrderAmount > candidate.maxOrderAmount
        ) {
            throw new Error('Payment method response contained invalid amount limits.');
        }

        if (seenIds.has(id) || seenCodes.has(code)) {
            throw new Error('Payment method response contained duplicate identity evidence.');
        }

        seenIds.add(id);
        seenCodes.add(code);
        admitted.push({
            id,
            code,
            displayName,
            displayNameAr: candidate.displayNameAr,
            description: candidate.description,
            descriptionAr: candidate.descriptionAr,
            supportsOnline: candidate.supportsOnline,
            supportsOffline: candidate.supportsOffline,
            icon: candidate.icon,
            minOrderAmount: candidate.minOrderAmount ?? undefined,
            maxOrderAmount: candidate.maxOrderAmount ?? undefined,
            fee,
            feeType,
            sortOrder,
            isAvailable: candidate.isAvailable,
            unavailableReasonCode: candidate.unavailableReasonCode ?? null,
        });
    }

    return admitted;
}

function findAdmittedAvailablePaymentMethod(
    methods: PaymentMethodDto[],
    code: string | null | undefined
): PaymentMethodDto | null {
    const normalizedCode = code?.trim().toUpperCase();
    if (!normalizedCode) {
        return null;
    }

    return methods.find((method) => method.code === normalizedCode && method.isAvailable) ?? null;
}

/**
 * Validate shipping address
 */
export function validateAddress(address: ShippingAddress): AddressValidationErrors {
    const errors: AddressValidationErrors = {};

    if (!address.fullName.trim()) {
        errors.fullName = 'Full name is required';
    } else if (address.fullName.trim().length < 3) {
        errors.fullName = 'Full name must be at least 3 characters';
    }

    if (!address.phone.trim()) {
        errors.phone = 'Phone number is required';
    } else if (!PHONE_REGEX.test(address.phone)) {
        errors.phone = 'Please enter a valid phone number';
    }

    if (!address.city.trim()) {
        errors.city = 'City is required';
    }

    if (!address.area.trim()) {
        errors.area = 'Area is required';
    }

    if (!address.addressLine.trim()) {
        errors.addressLine = 'Address is required';
    } else if (address.addressLine.trim().length < 10) {
        errors.addressLine = 'Please enter a more detailed address';
    }

    return errors;
}

/**
 * Check if address is valid
 */
export function isAddressValid(address: ShippingAddress): boolean {
    const errors = validateAddress(address);
    return Object.keys(errors).length === 0;
}

// ============================================================================
// State Types
// ============================================================================

export interface CheckoutState {
    // Current step
    step: CheckoutStep;

    // Shipping address
    address: ShippingAddress;

    // Shipping method
    shippingMethodId: string | null;
    shippingSelectionStatus: CheckoutSelectionStatus;
    staleShippingSelection: StaleCheckoutSelection | null;
    shippingMethods: ShippingMethod[];
    shippingMethodsLoading: boolean;
    shippingMethodsError: string | null;

    // Payment method
    paymentMethod: PaymentMethod | null;
    paymentSelectionStatus: CheckoutSelectionStatus;
    stalePaymentSelection: StaleCheckoutSelection | null;

    // Payment methods from POS Admin (Phase 10.4.D2)
    paymentMethods: PaymentMethodDto[];
    selectedPaymentMethodCode: string | null;
    paymentMethodsLoading: boolean;
    paymentMethodsError: string | null;

    // Authoritative pricing preview
    pricing: CheckoutPricingBreakdown | null;
    pricingLoading: boolean;
    pricingError: string | null;
    hasRequestedPricing: boolean;

    // Submission state
    isSubmitting: boolean;
    error: string | null;
    errorCode: string | null;

    // Order result
    orderResult: OrderResult | null;

    // Manual Wallet State
    manualWalletType: string | null;
    manualWalletPhoneNumber: string;
    manualWalletProofFileKey: string | null;
    merchantWalletNumber: string | null;
    manualWalletConfig: ManualWalletUploadConfig | null;
    manualWalletConfigError: string | null;
    manualWalletUploadToken: string | null;

    // Validation
    touchedFields: Set<keyof ShippingAddress>;
    showAllErrors: boolean;

    // Phase: Abandoned Checkout — session autosave
    checkoutSessionId: string | null;
    autosaveStatus: CheckoutAutosaveStatus;
    autosaveError: string | null;
    lastAutosavedAt: string | null;
}

export interface CheckoutActions {
    // Address actions
    setAddressField: <K extends keyof ShippingAddress>(key: K, value: ShippingAddress[K]) => void;
    setAddress: (address: ShippingAddress) => void;
    touchField: (key: keyof ShippingAddress) => void;

    // Shipping method actions
    setShippingMethod: (id: string) => void;
    fetchShippingMethods: () => Promise<void>;

    // Payment method actions
    setPaymentMethod: (method: PaymentMethod) => void;

    // Payment methods actions (Phase 10.4.D2)
    fetchPaymentMethods: () => Promise<void>;
    selectPaymentMethod: (code: string) => void;
    resetPaymentMethod: () => void;
    refreshPricingPreview: () => Promise<void>;

    // Navigation actions
    goToStep: (step: CheckoutStep) => void;
    goToNextStep: () => void;
    goToPreviousStep: () => void;

    // Submission
    submitCheckout: () => Promise<boolean>;

    // Error handling
    resetError: () => void;
    setError: (message: string, code?: string) => void;

    // Reset
    reset: () => void;

    // Manual Wallet Actions
    setManualWalletType: (type: string) => void;
    setManualWalletPhoneNumber: (phone: string) => void;
    setManualWalletProofFileKey: (fileKey: string | null) => void;
    fetchManualWalletConfig: () => Promise<void>;

    // Validation
    getAddressErrors: () => AddressValidationErrors;
    getFieldError: (key: keyof ShippingAddress) => string | undefined;
    isStepValid: (step: CheckoutStep) => boolean;
    setShowAllErrors: (show: boolean) => void;

    // Phase: Abandoned Checkout — autosave actions
    initCheckoutSession: () => Promise<void>;
    autosaveCheckoutSession: () => void;
    retryAutosaveCheckoutSession: () => Promise<void>;
}

// ============================================================================
// Initial State
// ============================================================================

const INITIAL_ADDRESS: ShippingAddress = {
    fullName: '',
    phone: '',
    city: '',
    area: '',
    addressLine: '',
    notes: '',
};

interface SensitiveCheckoutStateSnapshot {
    address: ShippingAddress;
}

function cloneShippingAddress(address: ShippingAddress): ShippingAddress {
    return {
        fullName: address.fullName,
        phone: address.phone,
        city: address.city,
        area: address.area,
        addressLine: address.addressLine,
        country: address.country,
        geography: address.geography,
        building: address.building,
        floor: address.floor,
        apartment: address.apartment,
        landmark: address.landmark,
        notes: address.notes,
    };
}

function getLocalStorageSafe(): Storage | null {
    if (typeof window === 'undefined') {
        return null;
    }

    try {
        return window.localStorage;
    } catch {
        return null;
    }
}

function getSessionStorageSafe(): Storage | null {
    if (typeof window === 'undefined') {
        return null;
    }

    try {
        return window.sessionStorage;
    } catch {
        return null;
    }
}

function getTenantScopedLocalStorageKey(key: string): string | null {
    return tryGetTenantScopedStorageKey(key);
}

function getTenantScopedSessionStorageKey(key: string): string | null {
    return tryGetTenantScopedStorageKey(key);
}

const checkoutStateStorage: StateStorage = {
    getItem: (name) => {
        const storage = getLocalStorageSafe();
        const scopedKey = getTenantScopedLocalStorageKey(name);
        if (!storage || !scopedKey) {
            return null;
        }

        return storage.getItem(scopedKey);
    },
    setItem: (name, value) => {
        const storage = getLocalStorageSafe();
        const scopedKey = getTenantScopedLocalStorageKey(name);
        if (!storage || !scopedKey) {
            return;
        }

        storage.setItem(scopedKey, value);
    },
    removeItem: (name) => {
        const storage = getLocalStorageSafe();
        const scopedKey = getTenantScopedLocalStorageKey(name);
        if (!storage || !scopedKey) {
            return;
        }

        storage.removeItem(scopedKey);
    },
};

function buildSensitiveCheckoutStateSnapshot(state: {
    address: ShippingAddress;
}): SensitiveCheckoutStateSnapshot {
    return {
        address: cloneShippingAddress(state.address),
    };
}

function isSensitiveCheckoutStateEmpty(snapshot: SensitiveCheckoutStateSnapshot): boolean {
    return !snapshot.address.fullName.trim() &&
        !snapshot.address.phone.trim() &&
        !snapshot.address.city.trim() &&
        !snapshot.address.area.trim() &&
        !snapshot.address.addressLine.trim() &&
        !(snapshot.address.building?.trim() ?? '') &&
        !(snapshot.address.floor?.trim() ?? '') &&
        !(snapshot.address.apartment?.trim() ?? '') &&
        !(snapshot.address.landmark?.trim() ?? '') &&
        !(snapshot.address.notes?.trim() ?? '') &&
        !snapshot.address.country?.trim() &&
        !snapshot.address.geography;
}

function readSensitiveCheckoutSessionStorage(): SensitiveCheckoutStateSnapshot | null {
    const storage = getSessionStorageSafe();
    if (!storage) {
        return null;
    }

    try {
        const storageKey = getTenantScopedSessionStorageKey(SENSITIVE_STORAGE_KEY);
        if (!storageKey) {
            return null;
        }

        const raw = storage.getItem(storageKey);
        if (!raw) {
            return null;
        }

        const parsed = JSON.parse(raw) as Partial<SensitiveCheckoutStateSnapshot>;
        const address = parsed.address && typeof parsed.address === 'object'
            ? parsed.address as Partial<ShippingAddress>
            : {};

        return {
            address: {
                ...INITIAL_ADDRESS,
                fullName: typeof address.fullName === 'string' ? address.fullName : INITIAL_ADDRESS.fullName,
                phone: typeof address.phone === 'string' ? address.phone : INITIAL_ADDRESS.phone,
                city: typeof address.city === 'string' ? address.city : INITIAL_ADDRESS.city,
                area: typeof address.area === 'string' ? address.area : INITIAL_ADDRESS.area,
                addressLine: typeof address.addressLine === 'string' ? address.addressLine : INITIAL_ADDRESS.addressLine,
                country: typeof address.country === 'string' ? address.country : INITIAL_ADDRESS.country,
                geography: address.geography && typeof address.geography === 'object' ? address.geography : undefined,
                building: typeof address.building === 'string' ? address.building : undefined,
                floor: typeof address.floor === 'string' ? address.floor : undefined,
                apartment: typeof address.apartment === 'string' ? address.apartment : undefined,
                landmark: typeof address.landmark === 'string' ? address.landmark : undefined,
                notes: typeof address.notes === 'string' ? address.notes : INITIAL_ADDRESS.notes,
            },
        };
    } catch {
        return null;
    }
}

function writeSensitiveCheckoutSessionStorage(snapshot: SensitiveCheckoutStateSnapshot): void {
    const storage = getSessionStorageSafe();
    if (!storage) {
        return;
    }

    try {
        const storageKey = getTenantScopedSessionStorageKey(SENSITIVE_STORAGE_KEY);
        if (!storageKey) {
            return;
        }

        if (isSensitiveCheckoutStateEmpty(snapshot)) {
            storage.removeItem(storageKey);
            return;
        }

        storage.setItem(storageKey, JSON.stringify(snapshot));
    } catch {
        // Storage may be unavailable or blocked by the browser.
    }
}

export function clearSensitiveCheckoutSessionStorage(): void {
    const storage = getSessionStorageSafe();
    if (!storage) {
        return;
    }

    try {
        const storageKey = getTenantScopedSessionStorageKey(SENSITIVE_STORAGE_KEY);
        if (storageKey) {
            storage.removeItem(storageKey);
        }
    } catch {
        // Storage may be unavailable or blocked by the browser.
    }
}

export function clearPersistedCheckoutStateStorage(): void {
    const storage = getLocalStorageSafe();
    if (!storage) {
        clearSensitiveCheckoutSessionStorage();
        return;
    }

    try {
        const storageKey = getTenantScopedLocalStorageKey(STORAGE_KEY);
        if (storageKey) {
            storage.removeItem(storageKey);
        }
    } catch {
        // Storage may be unavailable or blocked by the browser.
    }

    clearSensitiveCheckoutSessionStorage();
}

const INITIAL_STATE: CheckoutState = {
    step: 'shipping',
    address: INITIAL_ADDRESS,
    shippingMethodId: null,
    shippingSelectionStatus: 'none',
    staleShippingSelection: null,
    shippingMethods: [],
    shippingMethodsLoading: false,
    shippingMethodsError: null,
    paymentMethod: null,
    paymentSelectionStatus: 'none',
    stalePaymentSelection: null,
    paymentMethods: [],
    selectedPaymentMethodCode: null,
    paymentMethodsLoading: false,
    paymentMethodsError: null,
    pricing: null,
    pricingLoading: false,
    pricingError: null,
    hasRequestedPricing: false,
    isSubmitting: false,
    error: null,
    errorCode: null,
    orderResult: null,

    // Manual Wallet Defaults
    manualWalletType: null,
    manualWalletPhoneNumber: '',
    manualWalletProofFileKey: null,
    merchantWalletNumber: null,
    manualWalletConfig: null,
    manualWalletConfigError: null,
    manualWalletUploadToken: null,

    touchedFields: new Set(),
    showAllErrors: false,

    // Phase: Abandoned Checkout
    checkoutSessionId: null,
    autosaveStatus: 'idle',
    autosaveError: null,
    lastAutosavedAt: null,
};

// ============================================================================
// Store
// ============================================================================

export const useCheckoutStore = create<CheckoutState & CheckoutActions>()(
    persist(
        (set, get) => ({
            ...INITIAL_STATE,

            // ================================================================
            // Address Actions
            // ================================================================

            setAddressField: (key, value) => {
                set((state) => ({
                    address: { ...state.address, [key]: value },
                    touchedFields: new Set(state.touchedFields).add(key),
                }));
                // Phase: Abandoned Checkout — debounced autosave
                get().autosaveCheckoutSession();
            },

            setAddress: (address) => {
                set({ address });
                get().autosaveCheckoutSession();
            },

            touchField: (key) => {
                set((state) => ({
                    touchedFields: new Set(state.touchedFields).add(key),
                }));
            },

            // ================================================================
            // Shipping Method Actions
            // ================================================================

            setShippingMethod: (id) => {
                set({
                    shippingMethodId: id,
                    shippingSelectionStatus: 'valid',
                    staleShippingSelection: null,
                });
                get().autosaveCheckoutSession();
            },

            fetchShippingMethods: async () => {
                set({ shippingMethodsLoading: true, shippingMethodsError: null });

                try {
                    const response = await httpClient.get<ShippingMethod[]>(
                        CHECKOUT_ENDPOINTS.SHIPPING_METHODS,
                        {
                            headers: {
                                'Cache-Control': 'no-store',
                                Pragma: 'no-cache',
                            },
                        }
                    );

                    const methods = admitStorefrontShippingMethods(response.data);

                    const currentId = get().shippingMethodId;
                    const previousMethods = get().shippingMethods;
                    const previousStaleSelection = get().staleShippingSelection;
                    const methodIds = methods.map(m => m.id);
                    const hasStoredSelection = !!currentId;
                    const isCurrentSelectionValid = currentId ? methodIds.includes(currentId) : false;
                    const nextShippingMethodId = hasStoredSelection
                        ? currentId
                        : (methods[0]?.id ?? null);
                    const nextShippingSelectionStatus: CheckoutSelectionStatus = hasStoredSelection
                        ? (isCurrentSelectionValid ? 'valid' : 'stale')
                        : (nextShippingMethodId ? 'valid' : 'none');
                    const staleShippingSelection = hasStoredSelection && !isCurrentSelectionValid && currentId
                        ? {
                            value: currentId,
                            label:
                                previousMethods.find((method) => method.id === currentId)?.name ||
                                previousStaleSelection?.label ||
                                'Previous shipping selection',
                            message: STALE_SELECTION_ERROR_KEY,
                        }
                        : null;

                    set({
                        shippingMethods: methods,
                        shippingMethodsLoading: false,
                        shippingMethodsError: null,
                        shippingMethodId: nextShippingMethodId,
                        shippingSelectionStatus: nextShippingSelectionStatus,
                        staleShippingSelection,
                    });

                    if (!hasStoredSelection && nextShippingMethodId) {
                        get().autosaveCheckoutSession();
                    }

                    if (staleShippingSelection) {
                        emitCheckoutSelectionInvalidated('shipping', 'missing_from_latest_config', currentId);
                    }
                } catch (error) {
                    console.error('[CheckoutStore] Failed to fetch shipping methods:', error);
                    set({
                        shippingMethodsLoading: false,
                        shippingMethodsError: 'checkout.shippingMethodsLoadFailed',
                    });
                }
            },

            // ================================================================
            // Payment Method Actions
            // ================================================================

            setPaymentMethod: (method) => {
                get().selectPaymentMethod(method);
            },

            /**
             * Fetch payment methods from Storefront API (Phase 10.4.D2)
             * 
             * ARCHITECTURE:
             * - Payment methods are configured in POS Admin
             * - Only enabled methods are returned
             * - NO secrets are included
             */
            fetchPaymentMethods: async () => {
                set({ paymentMethodsLoading: true, paymentMethodsError: null });

                try {
                    const response = await httpClient.get<PaymentMethodDto[]>(
                        CHECKOUT_ENDPOINTS.PAYMENT_METHODS,
                        {
                            headers: {
                                'Cache-Control': 'no-store',
                                Pragma: 'no-cache',
                            },
                        }
                    );

                    const methods = admitStorefrontPaymentMethods(response.data);
                    const currentPaymentMethodCode = get().selectedPaymentMethodCode || get().paymentMethod;
                    const previousMethods = get().paymentMethods;
                    const previousStaleSelection = get().stalePaymentSelection;
                    const availableMethodCodes = methods
                        .filter((method) => method.isAvailable)
                        .map((method) => method.code);
                    const hasStoredSelection = !!currentPaymentMethodCode;
                    const isCurrentSelectionValid = currentPaymentMethodCode
                        ? availableMethodCodes.includes(currentPaymentMethodCode)
                        : false;
                    const nextPaymentMethodCode = hasStoredSelection
                        ? currentPaymentMethodCode
                        : (methods.find((method) => method.isAvailable)?.code ?? null);
                    const nextPaymentSelectionStatus: CheckoutSelectionStatus = hasStoredSelection
                        ? (isCurrentSelectionValid ? 'valid' : 'stale')
                        : (nextPaymentMethodCode ? 'valid' : 'none');
                    const stalePaymentSelection = hasStoredSelection && !isCurrentSelectionValid && currentPaymentMethodCode
                        ? {
                            value: currentPaymentMethodCode,
                            label:
                                previousMethods.find((method) => method.code === currentPaymentMethodCode)?.displayName ||
                                previousStaleSelection?.label ||
                                currentPaymentMethodCode,
                            message: STALE_SELECTION_ERROR_KEY,
                        }
                        : null;
                    const shouldKeepManualWalletState =
                        currentPaymentMethodCode === 'MANUALWALLET' || nextPaymentMethodCode === 'MANUALWALLET';

                    set({
                        paymentMethods: methods,
                        paymentMethodsLoading: false,
                        paymentMethodsError: null,
                        selectedPaymentMethodCode: nextPaymentMethodCode,
                        paymentMethod: nextPaymentMethodCode,
                        paymentSelectionStatus: nextPaymentSelectionStatus,
                        stalePaymentSelection,
                        manualWalletType: shouldKeepManualWalletState ? get().manualWalletType : null,
                        manualWalletPhoneNumber: shouldKeepManualWalletState
                            ? get().manualWalletPhoneNumber
                            : '',
                        manualWalletProofFileKey: shouldKeepManualWalletState
                            ? get().manualWalletProofFileKey
                            : null,
                    });

                    if (!hasStoredSelection && nextPaymentMethodCode) {
                        get().autosaveCheckoutSession();
                    }

                    if (stalePaymentSelection) {
                        emitCheckoutSelectionInvalidated('payment', 'missing_from_latest_config', currentPaymentMethodCode);
                    }

                    console.log('[CheckoutStore] Payment methods loaded:', methods.length);
                } catch (error) {
                    console.error('[CheckoutStore] Failed to fetch payment methods:', error);
                    set({
                        paymentMethodsLoading: false,
                        paymentMethodsError: 'checkout.paymentMethodsLoadFailed',
                    });
                }
            },

            selectPaymentMethod: (code) => {
                const normalizedCode = code.trim().toUpperCase();
                const admittedMethod = findAdmittedAvailablePaymentMethod(get().paymentMethods, normalizedCode);

                if (!admittedMethod) {
                    set({
                        selectedPaymentMethodCode: null,
                        paymentMethod: null,
                        paymentSelectionStatus: 'stale',
                        stalePaymentSelection: {
                            value: normalizedCode,
                            label: normalizedCode,
                            message: STALE_SELECTION_ERROR_KEY,
                        },
                        manualWalletType: null,
                        manualWalletPhoneNumber: '',
                        manualWalletProofFileKey: null,
                    });
                    emitCheckoutSelectionInvalidated('payment', 'client_selection_not_admitted', normalizedCode);
                    get().autosaveCheckoutSession();
                    return;
                }

                set((state) => ({
                    selectedPaymentMethodCode: admittedMethod.code,
                    paymentMethod: admittedMethod.code,
                    paymentSelectionStatus: 'valid',
                    stalePaymentSelection: null,
                    manualWalletType: admittedMethod.code === 'MANUALWALLET' ? state.manualWalletType : null,
                    manualWalletPhoneNumber: admittedMethod.code === 'MANUALWALLET'
                        ? state.manualWalletPhoneNumber
                        : '',
                    manualWalletProofFileKey: admittedMethod.code === 'MANUALWALLET'
                        ? state.manualWalletProofFileKey
                        : null,
                }));
                get().autosaveCheckoutSession();
            },

            resetPaymentMethod: () => {
                set({
                    selectedPaymentMethodCode: null,
                    paymentMethod: null,
                    paymentSelectionStatus: 'none',
                    stalePaymentSelection: null,
                    manualWalletType: null,
                    manualWalletPhoneNumber: '',
                    manualWalletProofFileKey: null,
                });
                get().autosaveCheckoutSession();
            },

            refreshPricingPreview: async () => {
                const cart = useCartStore.getState().cart;
                if (!cart?.cartId) {
                    cancelPricingPreviewRequest();
                    set({
                        pricing: null,
                        pricingLoading: false,
                        pricingError: null,
                        hasRequestedPricing: false,
                    });
                    return;
                }

                const storefrontDefaultCountryCode = useStoreConfigStore.getState().config.defaultCountryCode;
                const resolvedCountryCode = resolveCountryCode(
                    get().address.country,
                    storefrontDefaultCountryCode
                );

                const payload: CheckoutPricingPreviewRequest = {
                    cartId: cart.cartId,
                    shippingMethodId: get().shippingSelectionStatus === 'stale'
                        ? undefined
                        : (get().shippingMethodId || undefined),
                    paymentMethod: get().paymentSelectionStatus === 'stale'
                        ? undefined
                        : (get().selectedPaymentMethodCode || undefined),
                    shippingCountry: resolvedCountryCode || undefined,
                    shippingGeography: get().address.geography,
                };

                // Cancel any in-flight pricing request and start a new one
                cancelPricingPreviewRequest();
                const controller = new AbortController();
                pricingPreviewAbortController = controller;
                const requestSequence = ++pricingPreviewRequestSequence;

                set({
                    pricing: null,
                    pricingLoading: true,
                    pricingError: null,
                    hasRequestedPricing: true,
                });

                try {
                    const response = await httpClient.post<CheckoutPricingBreakdown>(
                        CHECKOUT_ENDPOINTS.PRICING,
                        payload,
                        { signal: controller.signal }
                    );

                    // Only update state if this is still the latest request
                    if (controller.signal.aborted || requestSequence !== pricingPreviewRequestSequence) {
                        return;
                    }

                    set({
                        pricing: response.data,
                        pricingLoading: false,
                        pricingError: null,
                    });
                } catch (error) {
                    // Ignore aborted requests
                    if (controller.signal.aborted) {
                        return;
                    }

                    // Only update error state if this is still the latest request
                    if (requestSequence !== pricingPreviewRequestSequence) {
                        return;
                    }

                    const axiosError = error as { response?: { data?: { code?: string; message?: string } } };
                    const errorCode = axiosError.response?.data?.code || null;
                    const responseMessage = axiosError.response?.data?.message || null;

                    set({
                        pricing: null,
                        pricingLoading: false,
                        pricingError:
                            errorCode === 'PROMOTION_CALCULATION_FAILED'
                                ? 'We could not safely calculate promotions for this checkout. Please try refreshing again.'
                                : responseMessage || 'Unable to load the latest checkout pricing.',
                    });
                } finally {
                    // Clean up controller reference if this is still the current one
                    if (pricingPreviewAbortController === controller) {
                        pricingPreviewAbortController = null;
                    }
                }
            },

            // ================================================================
            // Manual Wallet Actions
            // ================================================================

            setManualWalletType: (type) => set({ manualWalletType: type }),
            setManualWalletPhoneNumber: (phone) => set({ manualWalletPhoneNumber: phone }),
            setManualWalletProofFileKey: (fileKey) => set({ manualWalletProofFileKey: fileKey }),

            fetchManualWalletConfig: async () => {
                try {
                    const response = await httpClient.get<ManualWalletUploadConfig>(
                        '/api/storefront/payments/manual-wallet/config'
                    );
                    const walletTypes = Array.isArray(response.data.walletTypes)
                        ? response.data.walletTypes.filter((walletType) => typeof walletType.code === 'string' && walletType.code.trim())
                        : [];
                    const currentWalletType = get().manualWalletType;
                    const nextWalletType = currentWalletType && walletTypes.some(
                        (walletType) => walletType.code.trim().toLowerCase() === currentWalletType.trim().toLowerCase()
                    )
                        ? currentWalletType
                        : (walletTypes[0]?.code ?? null);

                    set({
                        merchantWalletNumber: response.data.walletNumber,
                        manualWalletConfig: {
                            ...response.data,
                            walletTypes,
                        },
                        manualWalletType: nextWalletType,
                        manualWalletConfigError: null,
                    });
                } catch (error) {
                    const normalized = normalizeError(error as any);
                    console.error('[CheckoutStore] Failed to fetch wallet config:', normalized);
                    set({
                        merchantWalletNumber: null,
                        manualWalletConfig: null,
                        manualWalletType: null,
                        manualWalletConfigError: 'checkout.manualWalletUnavailable',
                    });
                }
            },

            // ================================================================
            // Navigation Actions
            // ================================================================

            goToStep: (step) => {
                set({ step, showAllErrors: false });
            },

            goToNextStep: () => {
                const { step, isStepValid } = get();

                // Validate current step before proceeding
                if (!isStepValid(step)) {
                    set({ showAllErrors: true });
                    return;
                }

                if (step === 'shipping') {
                    set({ step: 'payment', showAllErrors: false });
                } else if (step === 'payment') {
                    set({ step: 'review', showAllErrors: false });
                }
            },

            goToPreviousStep: () => {
                const { step } = get();

                if (step === 'payment') {
                    set({ step: 'shipping' });
                } else if (step === 'review') {
                    set({ step: 'payment' });
                }
            },

            // ================================================================
            // Submission
            // ================================================================

            submitCheckout: async () => {
                const state = get();

                // Prevent double submission
                if (state.isSubmitting) {
                    return false;
                }

                clearCheckoutAutosaveTimer();
                cancelCheckoutAutosaveRequest();
                cancelCheckoutSessionInit();
                set({ isSubmitting: true, error: null, errorCode: null });

                try {
                    if (useCartStore.getState().cartMergeInProgress) {
                        await waitForCartMerge();
                    }

                    // Refresh cart to ensure we have the latest cart ID
                    await useCartStore.getState().fetchCart();
                    
                    // Refresh pricing after cart fetch to ensure we have up-to-date totals
                    // (fetchCart may have swapped in a different/merged cart)
                    await get().refreshPricingPreview();
                    
                    const latestState = get();

                    // Get fresh cart from cart store
                    const cart = useCartStore.getState().cart;
                    if (!cart?.cartId) {
                        set({ isSubmitting: false, error: 'checkout.cartEmpty' });
                        return false;
                    }

                    console.log('[CheckoutStore] Submitting checkout with cartId:', cart.cartId);

                    // Validate
                    if (!isAddressValid(latestState.address)) {
                        set({ isSubmitting: false, error: 'checkout.addressIncomplete', showAllErrors: true });
                        return false;
                    }

                    if (!latestState.shippingMethodId) {
                        set({ isSubmitting: false, error: 'checkout.shippingMethodRequired' });
                        return false;
                    }

                    if (latestState.shippingSelectionStatus === 'stale') {
                        set({
                            isSubmitting: false,
                            error: STALE_SELECTION_ERROR_KEY,
                            errorCode: 'STALE_SHIPPING_SELECTION',
                        });
                        return false;
                    }

                    if (!latestState.paymentMethod) {
                        set({ isSubmitting: false, error: 'checkout.paymentMethodRequired' });
                        return false;
                    }

                    if (latestState.paymentSelectionStatus === 'stale') {
                        set({
                            isSubmitting: false,
                            error: STALE_SELECTION_ERROR_KEY,
                            errorCode: 'STALE_PAYMENT_SELECTION',
                        });
                        return false;
                    }

                    const authStatus = selectAuthStatus(useAuthStore.getState());
                    if (authStatus === 'degraded') {
                        set({
                            isSubmitting: false,
                            error: 'checkout.authStateDegraded', // Error key for translation by consumers
                            errorCode: 'AUTH_STATE_DEGRADED',
                        });
                        return false;
                    }

                    if (latestState.paymentMethod === 'MANUALWALLET') {
                        const manualWalletErrors = validateManualWalletFields({
                            walletType: latestState.manualWalletType,
                            walletPhoneNumber: latestState.manualWalletPhoneNumber,
                            proofFileKey: latestState.manualWalletProofFileKey,
                            walletTypes: latestState.manualWalletConfig?.walletTypes,
                            requireKnownWalletType: true,
                        });

                        if (Object.keys(manualWalletErrors).length > 0) {
                            const manualWalletError = getFirstManualWalletError(manualWalletErrors);
                            set({
                                isSubmitting: false,
                                error: null,
                                errorCode: manualWalletError.code,
                                showAllErrors: true,
                            });
                            return false;
                        }
                    }

                    if (latestState.pricingLoading || !latestState.pricing || latestState.pricingError) {
                        set({
                            isSubmitting: false,
                            error: latestState.pricingError || 'Please wait for the latest checkout pricing before placing your order.',
                            errorCode: latestState.pricingError ? 'INVALID_EXPECTED_PRICING' : 'EXPECTED_PRICING_REQUIRED',
                        });
                        return false;
                    }

                    const selectedPaymentMethod = latestState.paymentMethods.find(
                        (method) => method.code === latestState.paymentMethod
                    );
                    if (!selectedPaymentMethod || !selectedPaymentMethod.isAvailable) {
                        set({
                            isSubmitting: false,
                            error: 'checkout.paymentMethodUnavailable',
                            errorCode: selectedPaymentMethod?.unavailableReasonCode ?? 'PAYMENT_METHOD_UNAVAILABLE',
                        });
                        return false;
                    }

                    const storefrontDefaultCountryCode = useStoreConfigStore.getState().config.defaultCountryCode;
                    const resolvedCountryCode = resolveCountryCode(
                        latestState.address.country,
                        storefrontDefaultCountryCode
                    );

                    if (!resolvedCountryCode) {
                        set({
                            isSubmitting: false,
                            error: 'checkout.shippingCountryInvalid',
                        });
                        return false;
                    }

                    // Map frontend address to backend AddressDto format
                    const shippingAddress = {
                        name: latestState.address.fullName,
                        phone: latestState.address.phone,
                        street: latestState.address.addressLine,
                        city: latestState.address.city,
                        state: latestState.address.area,
                        country: resolvedCountryCode,
                        geography: latestState.address.geography,
                        building: latestState.address.building,
                        floor: latestState.address.floor,
                        apartment: latestState.address.apartment,
                        landmark: latestState.address.landmark,
                    };
                    // Prepare Manual Wallet fields if applicable
                    let walletFields = {};
                    if (latestState.paymentMethod === 'MANUALWALLET') {
                        walletFields = {
                            walletType: latestState.manualWalletType,
                            walletPhoneNumber: latestState.manualWalletPhoneNumber,
                            proofFileKey: latestState.manualWalletProofFileKey
                        };
                    }

                    const finalRequest: CheckoutRequest = {
                        cartId: cart.cartId,
                        shippingAddress,
                        shippingMethodId: latestState.shippingMethodId,
                        paymentMethod: latestState.paymentMethod,
                        couponCode: cart.appliedCoupon?.code,
                        customerNotes: latestState.address.notes,
                        expectedPricing: buildExpectedPricing(latestState.pricing),
                        ...walletFields
                    };

                    // Get auth state to include customer ID
                    const customer = useAuthStore.getState().customer;
                    const isAuthenticated = selectIsAuthenticated(useAuthStore.getState());
                    const headers: Record<string, string> = {};

                    if (isAuthenticated && customer?.id) {
                        headers['X-Customer-Id'] = customer.id;
                        console.log('[CheckoutStore] Including X-Customer-Id:', customer.id);
                    }

                    const response = await httpClient.post<CheckoutResponse>(
                        CHECKOUT_ENDPOINTS.CHECKOUT,
                        finalRequest,
                        {
                            headers: {
                                ...headers,
                                'Cache-Control': 'no-store',
                                Pragma: 'no-cache',
                            },
                        }
                    );

                    const order = requireStorefrontPaymentSessionAuthority(response.data, latestState.paymentMethod, cart.cartId);

                    set({
                        isSubmitting: false,
                        autosaveStatus: 'idle',
                        autosaveError: null,
                        orderResult: {
                            orderId: order.orderId,
                            orderNumber: order.orderNumber,
                            paymentIntentId: order.paymentIntentId,
                            // Backend returns TotalAmount, not total
                            total: order.totalAmount ?? order.total ?? 0,
                            currency: order.currency,
                            checkoutToken: order.checkoutToken,
                            trackingToken: order.trackingToken,
                            promotionCalculationFailed: order.promotionCalculationFailed,
                            promotionWarningMessage: order.promotionWarningMessage,
                            // Normalize payment method to uppercase for consistent comparison
                            paymentMethod: latestState.paymentMethod?.toUpperCase() || 'COD',
                            stripeClientSecret: order.stripeClientSecret,
                            stripePublishableKey: order.stripePublishableKey,
                            // Paymob fields (Phase 10.4.D5)
                            paymobIframeUrl: order.paymobIframeUrl,
                            paymobPaymentKey: order.paymobPaymentKey,
                            // Fawry fields
                            referenceCode: order.referenceCode,
                            expiresAtUtc: order.expiresAtUtc,
                            // ManualWallet fields (Phase P3)
                            isManualWallet: order.isManualWallet,
                            merchantWalletNumber: order.merchantWalletNumber,
                            walletType: order.walletType,
                            walletInstructions: order.walletInstructions,
                        },
                        // Clear sensitive in-memory fields to prevent subscription from rehydrating them
                        address: INITIAL_ADDRESS,
                        manualWalletType: null,
                        manualWalletPhoneNumber: '',
                        manualWalletProofFileKey: null,
                        manualWalletUploadToken: null,
                        checkoutSessionId: null,
                    });
                    persistActiveCheckoutOrderContext({
                        orderId: order.orderId,
                        orderNumber: order.orderNumber,
                        checkoutToken: order.checkoutToken,
                        trackingToken: order.trackingToken,
                    });
                    clearSensitiveCheckoutSessionStorage();

                    return true;
                } catch (error: unknown) {
                    console.error('[CheckoutStore] Checkout failed:', error);

                    let errorMessage = 'Checkout failed. Please try again.';
                    let errorCode: string | null = null;
                    const normalized = normalizeError(error as any);

                    if (error && typeof error === 'object' && 'response' in error) {
                        const axiosError = error as { response?: { data?: { code?: string; message?: string } } };
                        errorCode = axiosError.response?.data?.code || null;

                        // Handle specific error codes
                        if (errorCode === 'OUT_OF_STOCK') {
                            errorMessage = 'Some items in your cart are out of stock. Please review your cart.';
                        } else if (errorCode === 'PRICE_CHANGED') {
                            errorMessage = 'Prices have changed. Please review your order.';
                        } else if (errorCode === 'PHONE_BLOCKED') {
                            errorMessage = 'This phone number cannot place new orders. Please contact the store for help.';
                        } else if (errorCode === 'MANUAL_WALLET_TYPE_REQUIRED') {
                            errorMessage = 'Please choose the wallet type you used for the transfer.';
                        } else if (errorCode === 'MANUAL_WALLET_TYPE_INVALID') {
                            errorMessage = 'Please choose a supported wallet type before continuing.';
                        } else if (errorCode === 'MANUAL_WALLET_PHONE_REQUIRED') {
                            errorMessage = 'Please enter the wallet phone number used for the transfer.';
                        } else if (errorCode === 'INVALID_MANUAL_WALLET_PHONE') {
                            errorMessage = 'Please enter a valid wallet phone number.';
                        } else if (errorCode === 'MANUAL_WALLET_PROOF_REQUIRED') {
                            errorMessage = 'Please upload a transfer proof before placing the order.';
                        } else if (errorCode === 'CHECKOUT_IDEMPOTENCY_CONFLICT') {
                            errorMessage = axiosError.response?.data?.message ||
                                'Your checkout details have changed. Please try again.';
                        } else if (errorCode === 'CHECKOUT_IN_PROGRESS') {
                            errorMessage = 'A checkout is already being processed. Please wait a moment and try again.';
                        } else if (errorCode === 'PROMOTION_CALCULATION_FAILED') {
                            errorMessage = 'We could not safely calculate promotions for this checkout. Please refresh pricing and try again.';
                            await get().refreshPricingPreview();
                        } else if (errorCode === 'CHECKOUT_TOTAL_MISMATCH') {
                            errorMessage = 'Checkout totals changed. We refreshed the latest pricing. Please review the updated amount before placing your order again.';
                            await get().refreshPricingPreview();
                        } else if (errorCode === 'CART_NOT_FOUND') {
                            // Cart expired or was cleared - try to refresh
                            errorMessage = 'Your cart session has expired. Please add items to your cart again.';
                            // Trigger cart refresh
                            try {
                                await useCartStore.getState().fetchCart();
                            } catch {
                                // Ignore refresh error
                            }
                        } else if (errorCode?.startsWith('COUPON_')) {
                            errorMessage = axiosError.response?.data?.message || 'The coupon on this checkout is no longer valid.';
                        } else if (axiosError.response?.data?.message) {
                            errorMessage = axiosError.response.data.message;
                        }
                    } else if (normalized.isNetworkError) {
                        errorCode = 'NETWORK_ERROR';
                        errorMessage = 'Network error while placing your order. We kept your checkout details so you can safely retry.';
                    }

                    set({
                        isSubmitting: false,
                        error: errorMessage,
                        errorCode,
                    });

                    return false;
                }
            },

            // ================================================================
            // Error Handling
            // ================================================================

            resetError: () => {
                set({ error: null, errorCode: null });
            },

            setError: (message, code) => {
                set({ error: message, errorCode: code || null });
            },

            // ================================================================
            // Reset
            // ================================================================

            reset: () => {
                clearCheckoutAutosaveTimer();
                cancelCheckoutAutosaveRequest();
                cancelCheckoutSessionInit();
                cancelPricingPreviewRequest();
                clearSensitiveCheckoutSessionStorage();
                set({
                    ...INITIAL_STATE,
                    touchedFields: new Set(),
                });
            },

            // ================================================================
            // Validation
            // ================================================================

            getAddressErrors: () => {
                return validateAddress(get().address);
            },

            getFieldError: (key) => {
                const state = get();
                // Notes field doesn't have validation errors
                if (key === 'notes') return undefined;
                if (!state.showAllErrors && !state.touchedFields.has(key)) {
                    return undefined;
                }
                const errors = validateAddress(state.address);
                // Type assertion needed because 'notes' is excluded from AddressValidationErrors
                return (errors as Record<string, string | undefined>)[key];
            },

            isStepValid: (step) => {
                const state = get();

                switch (step) {
                    case 'shipping':
                        return isAddressValid(state.address) &&
                            !!state.shippingMethodId &&
                            state.shippingSelectionStatus !== 'stale';
                    case 'payment': {
                        const selectedPaymentMethod = state.paymentMethods.find(
                            (method) => method.code === state.paymentMethod
                        );
                        if (
                            state.shippingSelectionStatus === 'stale' ||
                            state.paymentSelectionStatus === 'stale' ||
                            !selectedPaymentMethod ||
                            !selectedPaymentMethod.isAvailable
                        ) {
                            return false;
                        }

                        if (state.paymentMethod === 'MANUALWALLET') {
                            return !!state.paymentMethod && isManualWalletSelectionValid({
                                walletType: state.manualWalletType,
                                walletPhoneNumber: state.manualWalletPhoneNumber,
                                proofFileKey: state.manualWalletProofFileKey,
                                walletTypes: state.manualWalletConfig?.walletTypes,
                            });
                        }
                        return !!state.paymentMethod;
                    }
                    case 'review': {
                        const baseValid = isAddressValid(state.address) &&
                            !!state.shippingMethodId &&
                            !!state.paymentMethod &&
                            state.shippingSelectionStatus !== 'stale' &&
                            state.paymentSelectionStatus !== 'stale' &&
                            !!state.pricing &&
                            !state.pricingLoading &&
                            !state.pricingError;
                        const reviewPaymentMethod = state.paymentMethods.find(
                            (method) => method.code === state.paymentMethod
                        );

                        if (!reviewPaymentMethod || !reviewPaymentMethod.isAvailable) {
                            return false;
                        }

                        if (state.paymentMethod === 'MANUALWALLET') {
                            return baseValid && isManualWalletSelectionValid({
                                walletType: state.manualWalletType,
                                walletPhoneNumber: state.manualWalletPhoneNumber,
                                proofFileKey: state.manualWalletProofFileKey,
                                walletTypes: state.manualWalletConfig?.walletTypes,
                            });
                        }
                        return baseValid;
                    }
                    default:
                        return false;
                }
            },

            setShowAllErrors: (show) => {
                set({ showAllErrors: show });
            },

            // ================================================================
            // Phase: Abandoned Checkout — Session Autosave
            // ================================================================

            /**
             * Create or resume a checkout session.
             * Called once when the checkout page mounts.
             * Fire-and-forget — never blocks checkout UX.
             */
            initCheckoutSession: async () => {
                await ensureCheckoutSessionInitialized();
            },

            /**
             * Debounced autosave of checkout session fields.
             * Sends a PATCH with current form state.
             * Fire-and-forget — errors are silently ignored.
             */
            autosaveCheckoutSession: () => {
                scheduleCheckoutAutosave();
            },

            retryAutosaveCheckoutSession: async () => {
                useCheckoutStore.setState({
                    autosaveStatus: 'unsaved',
                    autosaveError: null,
                });
                await performCheckoutAutosave();
            },
        }),
        {
            name: STORAGE_KEY,
            version: 5,
            storage: createJSONStorage(() => checkoutStateStorage),
            migrate: (persistedState) => {
                const state = (persistedState ?? {}) as Partial<CheckoutState>;

                return {
                    ...INITIAL_STATE,
                    shippingMethodId: state.shippingMethodId ?? INITIAL_STATE.shippingMethodId,
                    paymentMethod: state.paymentMethod ?? INITIAL_STATE.paymentMethod,
                    selectedPaymentMethodCode: state.selectedPaymentMethodCode ?? INITIAL_STATE.selectedPaymentMethodCode,
                    step: state.step ?? INITIAL_STATE.step,
                };
            },
            partialize: (state) => ({
                // Only persist essential data, not loading states
                shippingMethodId: state.shippingMethodId,
                paymentMethod: state.paymentMethod,
                selectedPaymentMethodCode: state.selectedPaymentMethodCode,
                step: state.step,
            }),
        }
    )
);

const sensitiveCheckoutState = readSensitiveCheckoutSessionStorage();
if (sensitiveCheckoutState) {
    useCheckoutStore.setState({
        address: cloneShippingAddress(sensitiveCheckoutState.address),
    });
}

useCheckoutStore.subscribe((state, prevState) => {
    // Always persist sensitive fields to sessionStorage
    writeSensitiveCheckoutSessionStorage(buildSensitiveCheckoutStateSnapshot(state));

    // Trigger server autosave when sensitive fields change
    if (prevState) {
        const sensitiveFieldsChanged =
            state.manualWalletType !== prevState.manualWalletType ||
            state.manualWalletPhoneNumber !== prevState.manualWalletPhoneNumber ||
            state.manualWalletProofFileKey !== prevState.manualWalletProofFileKey;

        if (sensitiveFieldsChanged) {
            scheduleCheckoutAutosave();
        }
    }
});

// ============================================================================
// Selectors
// ============================================================================

export const selectCheckoutStep = (state: CheckoutState) => state.step;
export const selectShippingAddress = (state: CheckoutState) => state.address;
export const selectShippingMethodId = (state: CheckoutState) => state.shippingMethodId;
export const selectShippingSelectionStatus = (state: CheckoutState) => state.shippingSelectionStatus;
export const selectStaleShippingSelection = (state: CheckoutState) => state.staleShippingSelection;
export const selectShippingMethods = (state: CheckoutState) => state.shippingMethods;
export const selectPaymentMethod = (state: CheckoutState) => state.paymentMethod;
export const selectIsSubmitting = (state: CheckoutState) => state.isSubmitting;
export const selectCheckoutError = (state: CheckoutState) => state.error;
export const selectErrorCode = (state: CheckoutState) => state.errorCode;
export const selectOrderResult = (state: CheckoutState) => state.orderResult;
export const selectShippingMethodsLoading = (state: CheckoutState) => state.shippingMethodsLoading;
export const selectShippingMethodsError = (state: CheckoutState) => state.shippingMethodsError;
export const selectCheckoutAutosaveStatus = (state: CheckoutState) => state.autosaveStatus;
export const selectCheckoutAutosaveError = (state: CheckoutState) => state.autosaveError;
export const selectCheckoutLastAutosavedAt = (state: CheckoutState) => state.lastAutosavedAt;
export const selectPricingPreview = (state: CheckoutState) => state.pricing;
export const selectPricingLoading = (state: CheckoutState) => state.pricingLoading;
export const selectPricingError = (state: CheckoutState) => state.pricingError;
export const selectHasRequestedPricing = (state: CheckoutState) => state.hasRequestedPricing;
export const selectManualWalletConfig = (state: CheckoutState) => state.manualWalletConfig;
export const selectManualWalletConfigError = (state: CheckoutState) => state.manualWalletConfigError;
export const selectCheckoutShowAllErrors = (state: CheckoutState) => state.showAllErrors;

/**
 * Get selected shipping method
 */
export const selectSelectedShippingMethod = (state: CheckoutState): ShippingMethod | null => {
    const { shippingMethods, shippingMethodId } = state;
    return shippingMethods.find(m => m.id === shippingMethodId) || null;
};

/**
 * Calculate shipping cost
 */
export const selectShippingCost = (state: CheckoutState): number => {
    return state.pricing?.shipping ?? 0;
};

// ============================================================================
// Payment Methods Selectors (Phase 10.4.D2)
// ============================================================================

export const selectPaymentMethods = (state: CheckoutState) => state.paymentMethods;
export const selectSelectedPaymentMethodCode = (state: CheckoutState) => state.selectedPaymentMethodCode;
export const selectPaymentSelectionStatus = (state: CheckoutState) => state.paymentSelectionStatus;
export const selectStalePaymentSelection = (state: CheckoutState) => state.stalePaymentSelection;
export const selectPaymentMethodsLoading = (state: CheckoutState) => state.paymentMethodsLoading;
export const selectPaymentMethodsError = (state: CheckoutState) => state.paymentMethodsError;

/**
 * Get selected payment method object
 */
export const selectSelectedPaymentMethod = (state: CheckoutState): PaymentMethodDto | null => {
    const { paymentMethods, selectedPaymentMethodCode } = state;
    return paymentMethods.find(m => m.code === selectedPaymentMethodCode) || null;
};

/**
 * Get payment method fee (display only)
 */
export const selectPaymentFee = (state: CheckoutState): number => {
    return state.pricing?.paymentFee ?? 0;
};

/**
 * Check if a payment method is selected
 */
export const selectIsPaymentMethodSelected = (state: CheckoutState): boolean => {
    return !!state.selectedPaymentMethodCode;
};
