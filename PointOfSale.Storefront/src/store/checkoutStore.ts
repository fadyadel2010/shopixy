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
import { httpClient, normalizeError, isRetryableError, isRequestCanceled } from '@/services/api';
import { useCartStore, waitForCartMerge } from './cartStore';
import { useAuthStore, selectIsAuthenticated, selectAuthStatus } from '@/auth/store/authStore';
import { useStoreConfigStore } from './storeConfigStore';
import { resolveCountryCode } from '@/utils/country';
import { persistActiveCheckoutOrderContext } from '@/features/checkout/activeCheckoutOrderContext';
import { requireStorefrontPaymentSessionAuthority } from '@/features/checkout/paymentSessionAuthority';
import { emitCheckoutSelectionInvalidated } from '@/features/checkout/checkoutSelectionTelemetry';
import { tryGetTenantScopedStorageKey } from '@/bootstrap/tenantScopedStorage';
import { getEffectiveCheckoutFields } from '@/utils/checkoutFieldRules';
import type {
    CheckoutStep,
    ShippingAddress,
    PaymentMethod,
    PaymentMethodDto,
    ShippingMethod,
    CheckoutRequest,
    CheckoutResponse,
    CheckoutDependencyStatus,
    CheckoutExpectedPricing,
    CheckoutPricingBreakdown,
    CheckoutPricingPreviewRequest,
    CheckoutFieldDefinition,
    CheckoutFieldDefinitionsResponse,
    CheckoutFieldType,
    CheckoutAttachment,
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
    SHIPPING_METHODS_PREVIEW: '/api/storefront/shipping-methods/preview',
    SHIPPING_METHODS_SELECT: '/api/storefront/shipping-methods/select',
    PAYMENT_METHODS: '/api/storefront/payment-methods',
    CHECKOUT_FIELDS: '/api/storefront/checkout-fields',
    CHECKOUT_ATTACHMENT_LEASE: '/api/storefront/checkout/attachments/upload-lease',
    CHECKOUT_ATTACHMENT_CONFIRM: '/api/storefront/checkout/attachments/confirm',
    CHECKOUT_ATTACHMENT_REMOVE: (id: string) => `/api/storefront/checkout/attachments/${id}`,
    // Phase: Abandoned Checkout — autosave session endpoints
    CHECKOUT_SESSION: '/api/storefront/checkout-session',
    CHECKOUT_SESSION_UPDATE: (id: string) => `/api/storefront/checkout-session/${id}`,
} as const;

export type CheckoutAutosaveStatus = 'idle' | 'unsaved' | 'saving' | 'saved' | 'error';
export type CheckoutSelectionStatus = 'none' | 'valid' | 'stale';

const CHECKOUT_DEPENDENCY_STATUSES = new Set<CheckoutDependencyStatus>([
    'AVAILABLE',
    'EMPTY',
    'NOT_APPLICABLE',
    'VALIDATION_REQUIRED',
    'UNAVAILABLE',
    'ERROR',
]);

interface StaleCheckoutSelection {
    value: string;
    label: string;
    message: string;
}

type WarehouseResolutionStatus = 'RESOLVED' | 'LOCKED' | 'ORDERED' | 'INVALIDATED' | 'EXPIRED' | 'FAILED' | 'UNRESOLVED';

interface StorefrontShippingMethodsPreviewAdmission {
    status: CheckoutDependencyStatus;
    methods: ShippingMethod[];
}

interface StorefrontShippingMethodSelectionAdmission {
    warehouseResolutionId: string;
    warehouseResolutionVersion: number;
    warehouseResolutionStatus: WarehouseResolutionStatus;
    warehouseResolutionExpiresAtUtc: string | null;
}

interface CheckoutSessionResponseDto {
    sessionId?: string | null;
    manualWalletUploadToken?: string | null;
}

interface EnsureCheckoutSessionOptions {
    throwOnError?: boolean;
}

interface FetchShippingMethodsOptions {
    force?: boolean;
}

interface ShippingMethodSelectionOptions {
    previewSequence?: number;
}

let checkoutAutosaveTimer: ReturnType<typeof setTimeout> | null = null;
let checkoutAutosaveAbortController: AbortController | null = null;
let checkoutAutosaveRequestSequence = 0;
let checkoutSessionInitPromise: Promise<string | null> | null = null;
let checkoutSessionInitCartId: string | null = null;
let checkoutSessionInitAbortController: AbortController | null = null;
let pricingPreviewAbortController: AbortController | null = null;
let pricingPreviewRequestSequence = 0;
let checkoutFieldsAbortController: AbortController | null = null;
let checkoutFieldsRequestSequence = 0;
let shippingMethodsAbortController: AbortController | null = null;
let shippingMethodsRequestSequence = 0;
let shippingMethodsInFlightKey: string | null = null;
let shippingMethodsInFlightPromise: Promise<void> | null = null;
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
    checkoutSessionInitCartId = null;
}

function clearCheckoutSessionAuthorityForCartChange(): void {
    useCheckoutStore.setState({
        checkoutSessionId: null,
        checkoutSessionCartId: null,
        manualWalletUploadToken: null,
        manualWalletProofFileKey: null,
        warehouseResolutionId: null,
        warehouseResolutionVersion: null,
        warehouseResolutionStatus: null,
        warehouseResolutionExpiresAtUtc: null,
    });
}

function cancelPricingPreviewRequest(): void {
    if (pricingPreviewAbortController) {
        pricingPreviewAbortController.abort();
        pricingPreviewAbortController = null;
    }
}

function isWarehouseResolutionExpired(expiresAtUtc: string | null): boolean {
    if (!expiresAtUtc) {
        return false;
    }

    const timestamp = Date.parse(expiresAtUtc);
    return Number.isFinite(timestamp) && timestamp <= Date.now();
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
    return isRequestCanceled(error);
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

async function ensureCheckoutSessionInitialized(options: EnsureCheckoutSessionOptions = {}): Promise<string | null> {
    const cart = useCartStore.getState().cart;
    const currentCartId = cart?.cartId ?? null;
    if (!currentCartId) {
        return null;
    }

    const state = useCheckoutStore.getState();
    if (state.checkoutSessionId && state.manualWalletUploadToken && state.checkoutSessionCartId === currentCartId) {
        return state.checkoutSessionId;
    }

    if (state.checkoutSessionId && state.checkoutSessionCartId !== currentCartId) {
        clearCheckoutSessionAuthorityForCartChange();
    }

    if (checkoutSessionInitPromise) {
        if (checkoutSessionInitCartId === currentCartId) {
            try {
                return await checkoutSessionInitPromise;
            } catch (error) {
                if (options.throwOnError) {
                    throw error;
                }
                return null;
            }
        }

        cancelCheckoutSessionInit();
    }

    // Capture the cart at the start to validate response is still relevant.
    const capturedCart = cart as NonNullable<typeof cart>;
    const capturedCartId = currentCartId;

    let initPromise!: Promise<string | null>;
    initPromise = (async () => {

        // Create abort controller for this session init request
        checkoutSessionInitAbortController?.abort();
        const controller = new AbortController();
        checkoutSessionInitAbortController = controller;

        try {
            const response = await httpClient.post<CheckoutSessionResponseDto>(
                CHECKOUT_ENDPOINTS.CHECKOUT_SESSION,
                {
                    cartId: capturedCart.cartId,
                    itemsSnapshotJson: serializeWithSortedKeys(getCheckoutItemsSnapshot()),
                    cartTotal: capturedCart.grandTotal || capturedCart.subtotal || 0,
                    currency: capturedCart.currency || undefined,
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
                checkoutSessionCartId: capturedCartId,
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
            throw error;
        } finally {
            if (checkoutSessionInitAbortController === controller) {
                checkoutSessionInitAbortController = null;
            }
            if (checkoutSessionInitPromise === initPromise) {
                checkoutSessionInitPromise = null;
                checkoutSessionInitCartId = null;
            }
        }
    })();

    checkoutSessionInitPromise = initPromise;
    checkoutSessionInitCartId = capturedCartId;

    try {
        return await initPromise;
    } catch (error) {
        if (options.throwOnError) {
            throw error;
        }
        return null;
    }
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

const PHONE_ALLOWED_CHARACTERS_REGEX = /^[\d\s\-+()]+$/;
const hasPhoneDigitCount = (value: string, minimum: number) => {
    const digitCount = Array.from(value).filter((character) => /\d/.test(character)).length;
    return digitCount >= minimum && digitCount <= 20 && PHONE_ALLOWED_CHARACTERS_REGEX.test(value);
};
const isValidCheckoutPhone = (value: string) => hasPhoneDigitCount(value.trim(), 7);
const isValidManualWalletPhone = (value: string) => hasPhoneDigitCount(value.trim(), 8);
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
    } else if (!isValidManualWalletPhone(values.walletPhoneNumber)) {
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

function cancelCheckoutFieldsRequest(): void {
    checkoutFieldsAbortController?.abort();
    checkoutFieldsAbortController = null;
    checkoutFieldsRequestSequence += 1;
}

function cancelShippingMethodsRequest(): void {
    shippingMethodsAbortController?.abort();
    shippingMethodsAbortController = null;
    shippingMethodsRequestSequence += 1;
    shippingMethodsInFlightKey = null;
    shippingMethodsInFlightPromise = null;
}

function buildShippingMethodsRequestKey(
    cart: { cartId?: string | null; lastUpdatedUtc?: string | null },
    checkoutSessionId: string
): string {
    return [cart.cartId ?? '', checkoutSessionId, cart.lastUpdatedUtc ?? ''].join('|');
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

function normalizeCheckoutDependencyStatus(value: unknown, fallback: CheckoutDependencyStatus): CheckoutDependencyStatus {
    if (typeof value !== 'string') {
        return fallback;
    }

    const normalized = value.trim().toUpperCase() as CheckoutDependencyStatus;
    return CHECKOUT_DEPENDENCY_STATUSES.has(normalized) ? normalized : fallback;
}

function classifyCheckoutMethodLoadFailure(error: unknown): { status: CheckoutDependencyStatus; message: string } {
    const normalized = normalizeError(error as any);
    const candidate = error as { code?: string; message?: string };

    if (normalized.status === 400 || normalized.status === 401 || normalized.status === 403) {
        return {
            status: 'VALIDATION_REQUIRED',
            message: normalized.message || 'Checkout needs more information before loading options.',
        };
    }

    if (normalized.retryable || candidate.code === 'ECONNABORTED' || /timeout/i.test(candidate.message || '')) {
        return {
            status: 'UNAVAILABLE',
            message: normalized.status
                ? (normalized.message || 'Checkout options are temporarily unavailable. Please retry.')
                : 'Checkout options are temporarily unavailable. Please retry.',
        };
    }

    return {
        status: 'ERROR',
        message: normalized.message || candidate.message || 'Checkout options could not be loaded.',
    };
}

function requireStorefrontShippingTenantScope(value: unknown): string {
    if (typeof value !== 'string' || !UUID_REGEX.test(value)) {
        throw new Error('Shipping method response could not be admitted without tenant scope.');
    }

    return value;
}

function admitStorefrontShippingMethodList(payload: unknown, expectedTenantId: string): ShippingMethod[] {
    if (!Array.isArray(payload)) {
        throw new Error('Shipping method response failed admission.');
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
            carrierCode: optionalShippingString(candidate.carrierCode),
            serviceLevel: optionalShippingString(candidate.serviceLevel),
            trackingUrlTemplate: optionalShippingString(candidate.trackingUrlTemplate),
        };
    });
}

function admitStorefrontShippingMethods(payload: unknown): { status: CheckoutDependencyStatus; methods: ShippingMethod[] } {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Shipping method response failed admission.');
    }

    const candidate = payload as { tenantId?: unknown; status?: unknown; shippingMethods?: unknown };
    const expectedTenantId = requireStorefrontShippingTenantScope(candidate.tenantId);
    const methods = admitStorefrontShippingMethodList(candidate.shippingMethods, expectedTenantId);

    return {
        status: normalizeCheckoutDependencyStatus(candidate.status, methods.length > 0 ? 'AVAILABLE' : 'EMPTY'),
        methods,
    };
}

function admitStorefrontShippingMethodsPreview(payload: unknown): StorefrontShippingMethodsPreviewAdmission {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Shipping method preview response failed admission.');
    }

    const candidate = payload as {
        tenantId?: unknown;
        status?: unknown;
        shippingMethodsStatus?: unknown;
        shippingMethods?: unknown;
    };
    return admitStorefrontShippingMethods({
        tenantId: candidate.tenantId,
        status: candidate.shippingMethodsStatus,
        shippingMethods: candidate.shippingMethods,
    });
}

function admitStorefrontShippingMethodSelection(payload: unknown): StorefrontShippingMethodSelectionAdmission {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Shipping method selection response failed admission.');
    }

    const candidate = payload as {
        warehouseResolutionId?: unknown;
        resolutionVersion?: unknown;
        status?: unknown;
        expiresAtUtc?: unknown;
    };
    if (typeof candidate.warehouseResolutionId !== 'string' || !UUID_REGEX.test(candidate.warehouseResolutionId)) {
        throw new Error('Shipping method selection response contained malformed warehouse resolution identity.');
    }

    if (!Number.isInteger(candidate.resolutionVersion) || Number(candidate.resolutionVersion) < 1) {
        throw new Error('Shipping method selection response contained malformed warehouse resolution version.');
    }

    const resolutionStatus = typeof candidate.status === 'string'
        ? candidate.status.trim().toUpperCase()
        : '';
    if (!['RESOLVED', 'LOCKED', 'ORDERED', 'INVALIDATED', 'EXPIRED', 'FAILED', 'UNRESOLVED'].includes(resolutionStatus)) {
        throw new Error('Shipping method preview response contained malformed warehouse resolution status.');
    }

    const expiresAtUtc = candidate.expiresAtUtc;
    if (expiresAtUtc !== null && expiresAtUtc !== undefined && typeof expiresAtUtc !== 'string') {
        throw new Error('Shipping method selection response contained malformed warehouse resolution expiry.');
    }
    if (typeof expiresAtUtc === 'string' && !Number.isFinite(Date.parse(expiresAtUtc))) {
        throw new Error('Shipping method selection response contained malformed warehouse resolution expiry.');
    }
    if (resolutionStatus === 'RESOLVED' && typeof expiresAtUtc !== 'string') {
        throw new Error('Shipping method selection response omitted warehouse resolution expiry.');
    }

    return {
        warehouseResolutionId: candidate.warehouseResolutionId,
        warehouseResolutionVersion: Number(candidate.resolutionVersion),
        warehouseResolutionStatus: resolutionStatus as WarehouseResolutionStatus,
        warehouseResolutionExpiresAtUtc: typeof expiresAtUtc === 'string' ? expiresAtUtc : null,
    };
}

function admitStorefrontPaymentMethodList(payload: unknown): PaymentMethodDto[] {
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

function admitStorefrontPaymentMethods(payload: unknown): { status: CheckoutDependencyStatus; methods: PaymentMethodDto[] } {
    if (Array.isArray(payload)) {
        const methods = admitStorefrontPaymentMethodList(payload);
        return {
            status: methods.length > 0 ? 'AVAILABLE' : 'EMPTY',
            methods,
        };
    }

    if (!payload || typeof payload !== 'object') {
        throw new Error('Payment method response failed admission.');
    }

    const candidate = payload as { status?: unknown; paymentMethods?: unknown };
    const methods = admitStorefrontPaymentMethodList(candidate.paymentMethods);

    return {
        status: normalizeCheckoutDependencyStatus(candidate.status, methods.length > 0 ? 'AVAILABLE' : 'EMPTY'),
        methods,
    };
}

const CHECKOUT_FIELD_TYPES = new Set<CheckoutFieldType>([
    'TEXT',
    'TEXTAREA',
    'NUMBER',
    'EMAIL',
    'PHONE',
    'SELECT',
    'RADIO',
    'CHECKBOX',
    'DATE',
    'IMAGE',
    'FILE',
    'MULTI_IMAGE',
]);
const CHECKOUT_FIELD_RULE_OPERATORS = new Set([
    'EQUALS', 'NOT_EQUALS', 'CONTAINS', 'NOT_CONTAINS', 'IS_EMPTY', 'IS_NOT_EMPTY',
    'GREATER_THAN', 'GREATER_THAN_OR_EQUAL', 'LESS_THAN', 'LESS_THAN_OR_EQUAL',
]);
const CHECKOUT_FIELD_RULE_ACTIONS = new Set(['SHOW', 'HIDE', 'REQUIRE', 'OPTIONAL']);

function admitConditionalRules(configuration: any) {
    if (!Array.isArray(configuration.conditionalRules)) return null;
    if (configuration.conditionalRules.length > 25) throw new Error('Checkout field response contains too many rules.');
    const seenRuleIds = new Set<string>();
    return configuration.conditionalRules.map((rule: any) => {
        if (!rule || typeof rule !== 'object' || typeof rule.ruleId !== 'string' || !rule.ruleId.trim() || seenRuleIds.has(rule.ruleId) ||
            (rule.conditionMode !== 'ALL' && rule.conditionMode !== 'ANY') || !Array.isArray(rule.actions) ||
            rule.actions.length < 1 || rule.actions.length > 2 || !rule.actions.every((action: unknown) => CHECKOUT_FIELD_RULE_ACTIONS.has(String(action))) ||
            !Array.isArray(rule.conditions) || rule.conditions.length < 1 || rule.conditions.length > 5) {
            throw new Error('Checkout field response contains malformed conditional rules.');
        }
        seenRuleIds.add(rule.ruleId);
        const actions = Array.from(new Set(rule.actions.map(String)));
        if ((actions.includes('SHOW') && actions.includes('HIDE')) || (actions.includes('REQUIRE') && actions.includes('OPTIONAL'))) {
            throw new Error('Checkout field response contains conflicting rule actions.');
        }
        const conditions = rule.conditions.map((condition: any) => {
            const operator = String(condition?.operator || '').toUpperCase();
            if (!condition || !UUID_REGEX.test(String(condition.sourceFieldId || '')) || !CHECKOUT_FIELD_RULE_OPERATORS.has(operator) ||
                (!['IS_EMPTY', 'IS_NOT_EMPTY'].includes(operator) && typeof condition.comparisonValue !== 'string')) {
                throw new Error('Checkout field response contains malformed rule conditions.');
            }
            return {
                sourceFieldId: String(condition.sourceFieldId),
                operator,
                comparisonValue: typeof condition.comparisonValue === 'string' ? condition.comparisonValue : null,
            };
        });
        return { ruleId: rule.ruleId, conditionMode: rule.conditionMode, actions, conditions };
    });
}

function admitCheckoutFieldList(payload: unknown): CheckoutFieldDefinition[] {
    if (!Array.isArray(payload)) {
        throw new Error('Checkout field response failed admission.');
    }

    const seenIds = new Set<string>();
    const seenSystemKeys = new Set<string>();

    return payload.map((item) => {
        if (!item || typeof item !== 'object') {
            throw new Error('Checkout field response contained malformed evidence.');
        }

        const candidate = item as Partial<CheckoutFieldDefinition>;
        const id = typeof candidate.id === 'string' ? candidate.id.trim() : '';
        const fieldKind = candidate.fieldKind;
        const fieldType = candidate.fieldType;
        const label = typeof candidate.label === 'string' ? candidate.label.trim() : '';
        const displayOrder = candidate.displayOrder;

        if (
            !UUID_REGEX.test(id) ||
            seenIds.has(id) ||
            (fieldKind !== 'SYSTEM' && fieldKind !== 'CUSTOM') ||
            !fieldType ||
            !CHECKOUT_FIELD_TYPES.has(fieldType) ||
            !label ||
            typeof candidate.isEnabled !== 'boolean' ||
            typeof candidate.isRequired !== 'boolean' ||
            !Number.isInteger(displayOrder) ||
            typeof displayOrder !== 'number' ||
            displayOrder < 0 ||
            !isOptionalString(candidate.systemFieldKey) ||
            !isOptionalString(candidate.helpText)
        ) {
            throw new Error('Checkout field response failed admission.');
        }
        const systemFieldKey = candidate.systemFieldKey?.trim() ?? '';
        if ((fieldKind === 'SYSTEM' && (!systemFieldKey || seenSystemKeys.has(systemFieldKey.toLowerCase()))) ||
            (fieldKind === 'CUSTOM' && systemFieldKey)) {
            throw new Error('Checkout field response contained invalid system field identity.');
        }

        seenIds.add(id);
        if (systemFieldKey) seenSystemKeys.add(systemFieldKey.toLowerCase());

        if (!candidate.configuration || typeof candidate.configuration !== 'object') {
            throw new Error('Checkout field response contained malformed configuration.');
        }
        const configuration = candidate.configuration;
        if (configuration.schemaVersion !== 1 || (candidate.isRequired && !candidate.isEnabled)) {
            throw new Error('Checkout field response contained unsupported configuration.');
        }
        const options = configuration.options;
        if ((fieldType === 'SELECT' || fieldType === 'RADIO') && (!Array.isArray(options) || options.length === 0)) {
            throw new Error('Checkout option field response did not contain options.');
        }
        if (Array.isArray(options)) {
            const optionValues = new Set<string>();
            for (const option of options) {
                if (!option || typeof option.value !== 'string' || !option.value.trim() || typeof option.label !== 'string' || !option.label.trim() || optionValues.has(option.value.toLowerCase())) {
                    throw new Error('Checkout field response contained malformed options.');
                }
                optionValues.add(option.value.toLowerCase());
            }
        }

        return {
            id,
            fieldKind,
            fieldType,
            systemFieldKey: systemFieldKey || null,
            label,
            helpText: candidate.helpText ?? null,
            isEnabled: candidate.isEnabled,
            isRequired: candidate.isRequired,
            displayOrder,
            configuration: {
                schemaVersion: typeof configuration.schemaVersion === 'number' ? configuration.schemaVersion : 1,
                options: Array.isArray(configuration.options)
                    ? configuration.options.map((option) => ({ value: option.value, label: option.label }))
                    : null,
                conditionalRules: admitConditionalRules(configuration),
                min: typeof configuration.min === 'number' ? configuration.min : null,
                max: typeof configuration.max === 'number' ? configuration.max : null,
                minLength: typeof configuration.minLength === 'number' ? configuration.minLength : null,
                maxLength: typeof configuration.maxLength === 'number' ? configuration.maxLength : null,
                allowedMimeTypes: Array.isArray(configuration.allowedMimeTypes)
                    ? configuration.allowedMimeTypes.filter((value) => typeof value === 'string')
                    : null,
                maxFileSize: typeof configuration.maxFileSize === 'number' ? configuration.maxFileSize : null,
                maxFiles: typeof configuration.maxFiles === 'number' ? configuration.maxFiles : null,
            },
        };
    }).sort((a, b) => a.displayOrder - b.displayOrder || a.label.localeCompare(b.label) || a.id.localeCompare(b.id));
}

function admitCheckoutFields(payload: unknown): { status: CheckoutDependencyStatus; fields: CheckoutFieldDefinition[]; totalCount: number; enabledCount: number } {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Checkout field response failed admission.');
    }

    const candidate = payload as CheckoutFieldDefinitionsResponse;
    const fields = admitCheckoutFieldList(candidate.fields);
    if (!Number.isInteger(candidate.totalCount) || candidate.totalCount < 0 ||
        !Number.isInteger(candidate.enabledCount) || candidate.enabledCount < 0 ||
        candidate.enabledCount !== fields.length || candidate.totalCount < candidate.enabledCount) {
        throw new Error('Checkout field response contained contradictory counts.');
    }
    const status = normalizeCheckoutDependencyStatus(candidate.status, fields.length > 0 ? 'AVAILABLE' : 'EMPTY');
    if ((status === 'AVAILABLE') !== (fields.length > 0)) {
        throw new Error('Checkout field response contained a contradictory status.');
    }

    return {
        status,
        fields,
        totalCount: candidate.totalCount,
        enabledCount: candidate.enabledCount,
    };
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
const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const isCanonicalCheckoutDate = (value: string) => {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) return false;
    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    if (year < 1 || year > 9999 || month < 1 || month > 12) return false;
    const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    const daysInMonth = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    return day >= 1 && day <= daysInMonth[month - 1];
};

const SYSTEM_FIELD_ADDRESS_KEY_MAP: Record<string, keyof ShippingAddress | 'notes'> = {
    full_name: 'fullName',
    phone: 'phone',
    email: 'email',
    city: 'city',
    area: 'area',
    street_address: 'addressLine',
    building: 'building',
    apartment: 'apartment',
    postal_code: 'postalCode',
    order_notes: 'notes',
};

function isDynamicCheckoutFieldsAvailable(fields?: CheckoutFieldDefinition[]): fields is CheckoutFieldDefinition[] {
    return Array.isArray(fields) && fields.length > 0;
}

function isUploadCheckoutField(fieldType: CheckoutFieldType): boolean {
    return fieldType === 'IMAGE' || fieldType === 'FILE' || fieldType === 'MULTI_IMAGE';
}

function parseCheckoutAttachments(value?: string): CheckoutAttachment[] {
    if (!value?.trim()) return [];
    try {
        const parsed = JSON.parse(value);
        return Array.isArray(parsed) ? parsed.filter((item) => item?.attachmentId) : [];
    } catch {
        return [];
    }
}

export function validateAddress(address: ShippingAddress, checkoutFields?: CheckoutFieldDefinition[]): AddressValidationErrors {
    const errors: AddressValidationErrors = {};

    if (isDynamicCheckoutFieldsAvailable(checkoutFields)) {
        for (const field of checkoutFields) {
            if (field.fieldKind !== 'SYSTEM' || !field.isEnabled || !field.systemFieldKey) {
                continue;
            }

            const addressKey = SYSTEM_FIELD_ADDRESS_KEY_MAP[field.systemFieldKey];
            if (!addressKey) {
                continue;
            }

            const value = (address[addressKey as keyof ShippingAddress] ?? '') as string;
            if (field.isRequired && !value.trim()) {
                errors[addressKey as keyof AddressValidationErrors] = `${field.label} is required`;
                continue;
            }

            if (value.trim()) {
                const absoluteMax = field.fieldType === 'TEXTAREA' ? 5000 : 1000;
                const maxLength = Math.min(field.configuration.maxLength ?? absoluteMax, absoluteMax);
                if (field.configuration.minLength != null && value.trim().length < field.configuration.minLength) {
                    errors[addressKey as keyof AddressValidationErrors] = `${field.label} is too short`;
                } else if (value.trim().length > maxLength) {
                    errors[addressKey as keyof AddressValidationErrors] = `${field.label} is too long`;
                }
            }
        }

        if (address.email?.trim() && !EMAIL_REGEX.test(address.email.trim())) {
            errors.email = 'Please enter a valid email address';
        }

        if (address.phone?.trim() && !isValidCheckoutPhone(address.phone)) {
            errors.phone = 'Please enter a valid phone number';
        }

        return errors;
    }

    if (!address.fullName.trim()) {
        errors.fullName = 'Full name is required';
    } else if (address.fullName.trim().length < 3) {
        errors.fullName = 'Full name must be at least 3 characters';
    }

    if (!address.phone.trim()) {
        errors.phone = 'Phone number is required';
    } else if (!isValidCheckoutPhone(address.phone)) {
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

export function validateCustomCheckoutFields(
    fields: CheckoutFieldDefinition[],
    values: Record<string, string>
): Record<string, string> {
    const errors: Record<string, string> = {};

    for (const field of fields) {
        if (field.fieldKind !== 'CUSTOM' || !field.isEnabled) {
            continue;
        }

        const rawValue = values[field.id] ?? '';
        if (isUploadCheckoutField(field.fieldType)) {
            const attachments = parseCheckoutAttachments(rawValue);
            const maxFiles = field.fieldType === 'MULTI_IMAGE' ? (field.configuration.maxFiles ?? 5) : 1;
            if (field.isRequired && attachments.length === 0) {
                errors[field.id] = `${field.label} is required`;
            } else if (attachments.length > maxFiles) {
                errors[field.id] = `${field.label} allows at most ${maxFiles} file(s)`;
            }
            continue;
        }

        const value = rawValue.trim();
        const isEmpty = field.fieldType === 'CHECKBOX'
            ? rawValue !== 'true'
            : !value;

        if (field.isRequired && isEmpty) {
            errors[field.id] = `${field.label} is required`;
            continue;
        }

        if (isEmpty) {
            continue;
        }

        const absoluteMax = field.fieldType === 'TEXTAREA' ? 5000 : 1000;
        const maxLength = Math.min(field.configuration.maxLength ?? absoluteMax, absoluteMax);
        if (field.configuration.minLength != null && value.length < field.configuration.minLength) {
            errors[field.id] = `${field.label} is too short`;
        } else if (value.length > maxLength) {
            errors[field.id] = `${field.label} is too long`;
        } else if (field.fieldType === 'EMAIL' && !EMAIL_REGEX.test(value)) {
            errors[field.id] = `Please enter a valid ${field.label.toLowerCase()}`;
        } else if (field.fieldType === 'PHONE' && !isValidCheckoutPhone(value)) {
            errors[field.id] = `Please enter a valid ${field.label.toLowerCase()}`;
        } else if (field.fieldType === 'NUMBER') {
            const numericValue = /^[-+]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(value) ? Number(value) : Number.NaN;
            if (!Number.isFinite(numericValue)) {
                errors[field.id] = `${field.label} must be a valid number`;
            } else if (field.configuration.min != null && numericValue < field.configuration.min) {
                errors[field.id] = `${field.label} is below the allowed minimum`;
            } else if (field.configuration.max != null && numericValue > field.configuration.max) {
                errors[field.id] = `${field.label} is above the allowed maximum`;
            }
        } else if (field.fieldType === 'SELECT' || field.fieldType === 'RADIO') {
            const optionValues = new Set((field.configuration.options ?? []).map((option) => option.value));
            if (!optionValues.has(value)) {
                errors[field.id] = `Please choose a valid ${field.label.toLowerCase()}`;
            }
        } else if (field.fieldType === 'DATE' && !isCanonicalCheckoutDate(value)) {
            errors[field.id] = `${field.label} must be a valid date`;
        }
    }

    return errors;
}

/**
 * Check if address is valid
 */
export function isAddressValid(address: ShippingAddress, checkoutFields?: CheckoutFieldDefinition[]): boolean {
    const errors = validateAddress(address, checkoutFields);
    return Object.keys(errors).length === 0;
}

function getEffectiveFieldsForState(state: CheckoutState): CheckoutFieldDefinition[] | undefined {
    return state.checkoutFieldsStatus === 'AVAILABLE' ||
        (state.checkoutFieldsStatus === 'EMPTY' && state.checkoutFieldsTotalCount > 0)
        ? getEffectiveCheckoutFields(state.checkoutFields, state.address, state.customFieldValues)
        : undefined;
}

function checkoutFieldsAreReady(state: CheckoutState): boolean {
    return state.checkoutFieldsStatus === 'AVAILABLE' || state.checkoutFieldsStatus === 'EMPTY';
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
    shippingMethodsStatus: CheckoutDependencyStatus;
    shippingMethodsLoading: boolean;
    shippingMethodsError: string | null;
    warehouseResolutionId: string | null;
    warehouseResolutionVersion: number | null;
    warehouseResolutionStatus: WarehouseResolutionStatus | null;
    warehouseResolutionExpiresAtUtc: string | null;

    // Payment method
    paymentMethod: PaymentMethod | null;
    paymentSelectionStatus: CheckoutSelectionStatus;
    stalePaymentSelection: StaleCheckoutSelection | null;

    // Payment methods from POS Admin (Phase 10.4.D2)
    paymentMethods: PaymentMethodDto[];
    paymentMethodsStatus: CheckoutDependencyStatus;
    selectedPaymentMethodCode: string | null;
    paymentMethodsLoading: boolean;
    paymentMethodsError: string | null;

    // Authoritative pricing preview
    pricing: CheckoutPricingBreakdown | null;
    pricingLoading: boolean;
    pricingError: string | null;
    hasRequestedPricing: boolean;

    // Dynamic checkout fields
    checkoutFields: CheckoutFieldDefinition[];
    checkoutFieldsStatus: CheckoutDependencyStatus;
    checkoutFieldsLoading: boolean;
    checkoutFieldsError: string | null;
    checkoutFieldsTotalCount: number;
    customFieldValues: Record<string, string>;
    touchedCustomFields: Set<string>;

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
    checkoutSessionCartId: string | null;
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
    setShippingMethod: (id: string, options?: ShippingMethodSelectionOptions) => Promise<void>;
    fetchShippingMethods: (options?: FetchShippingMethodsOptions) => Promise<void>;

    // Payment method actions
    setPaymentMethod: (method: PaymentMethod) => void;

    // Payment methods actions (Phase 10.4.D2)
    fetchPaymentMethods: () => Promise<void>;
    selectPaymentMethod: (code: string) => void;
    resetPaymentMethod: () => void;
    refreshPricingPreview: () => Promise<void>;
    fetchCheckoutFields: (force?: boolean) => Promise<void>;
    setCustomFieldValue: (fieldId: string, value: string) => void;
    touchCustomField: (fieldId: string) => void;
    getCustomFieldError: (fieldId: string) => string | undefined;

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
    customFieldValues: Record<string, string>;
}

function cloneShippingAddress(address: ShippingAddress): ShippingAddress {
    return {
        fullName: address.fullName,
        phone: address.phone,
        email: address.email,
        city: address.city,
        area: address.area,
        addressLine: address.addressLine,
        country: address.country,
        geography: address.geography,
        building: address.building,
        floor: address.floor,
        apartment: address.apartment,
        postalCode: address.postalCode,
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
    customFieldValues: Record<string, string>;
}): SensitiveCheckoutStateSnapshot {
    return {
        address: cloneShippingAddress(state.address),
        customFieldValues: { ...state.customFieldValues },
    };
}

function isSensitiveCheckoutStateEmpty(snapshot: SensitiveCheckoutStateSnapshot): boolean {
    return !snapshot.address.fullName.trim() &&
        !snapshot.address.phone.trim() &&
        !(snapshot.address.email?.trim() ?? '') &&
        !snapshot.address.city.trim() &&
        !snapshot.address.area.trim() &&
        !snapshot.address.addressLine.trim() &&
        !(snapshot.address.building?.trim() ?? '') &&
        !(snapshot.address.floor?.trim() ?? '') &&
        !(snapshot.address.apartment?.trim() ?? '') &&
        !(snapshot.address.postalCode?.trim() ?? '') &&
        !(snapshot.address.landmark?.trim() ?? '') &&
        !(snapshot.address.notes?.trim() ?? '') &&
        !snapshot.address.country?.trim() &&
        !snapshot.address.geography &&
        Object.values(snapshot.customFieldValues).every((value) => !value);
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
                email: typeof address.email === 'string' ? address.email : undefined,
                city: typeof address.city === 'string' ? address.city : INITIAL_ADDRESS.city,
                area: typeof address.area === 'string' ? address.area : INITIAL_ADDRESS.area,
                addressLine: typeof address.addressLine === 'string' ? address.addressLine : INITIAL_ADDRESS.addressLine,
                country: typeof address.country === 'string' ? address.country : INITIAL_ADDRESS.country,
                geography: address.geography && typeof address.geography === 'object' ? address.geography : undefined,
                building: typeof address.building === 'string' ? address.building : undefined,
                floor: typeof address.floor === 'string' ? address.floor : undefined,
                apartment: typeof address.apartment === 'string' ? address.apartment : undefined,
                postalCode: typeof address.postalCode === 'string' ? address.postalCode : undefined,
                landmark: typeof address.landmark === 'string' ? address.landmark : undefined,
                notes: typeof address.notes === 'string' ? address.notes : INITIAL_ADDRESS.notes,
            },
            customFieldValues: parsed.customFieldValues && typeof parsed.customFieldValues === 'object'
                ? Object.entries(parsed.customFieldValues).reduce<Record<string, string>>((values, [key, value]) => {
                    if (UUID_REGEX.test(key) && typeof value === 'string') values[key] = value;
                    return values;
                }, {})
                : {},
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
    shippingMethodsStatus: 'IDLE',
    shippingMethodsLoading: false,
    shippingMethodsError: null,
    warehouseResolutionId: null,
    warehouseResolutionVersion: null,
    warehouseResolutionStatus: null,
    warehouseResolutionExpiresAtUtc: null,
    paymentMethod: null,
    paymentSelectionStatus: 'none',
    stalePaymentSelection: null,
    paymentMethods: [],
    paymentMethodsStatus: 'IDLE',
    selectedPaymentMethodCode: null,
    paymentMethodsLoading: false,
    paymentMethodsError: null,
    pricing: null,
    pricingLoading: false,
    pricingError: null,
    hasRequestedPricing: false,
    checkoutFields: [],
    checkoutFieldsStatus: 'IDLE',
    checkoutFieldsLoading: false,
    checkoutFieldsError: null,
    checkoutFieldsTotalCount: 0,
    customFieldValues: {},
    touchedCustomFields: new Set(),
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
    checkoutSessionCartId: null,
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

            setShippingMethod: async (id, options = {}) => {
                set({
                    shippingMethodId: id,
                    shippingSelectionStatus: 'valid',
                    staleShippingSelection: null,
                    warehouseResolutionId: null,
                    warehouseResolutionVersion: null,
                    warehouseResolutionStatus: null,
                    warehouseResolutionExpiresAtUtc: null,
                });

                const checkoutSessionId = await ensureCheckoutSessionInitialized({ throwOnError: true });
                if (!checkoutSessionId) {
                    throw new Error('Checkout session is required before selecting shipping method.');
                }

                const response = await httpClient.post<unknown>(
                    CHECKOUT_ENDPOINTS.SHIPPING_METHODS_SELECT,
                    { checkoutSessionId, shippingMethodId: id },
                    {
                        headers: {
                            'Cache-Control': 'no-store',
                            Pragma: 'no-cache',
                        },
                    }
                );
                const admittedSelection = admitStorefrontShippingMethodSelection(response.data);
                if (options.previewSequence !== undefined && options.previewSequence !== shippingMethodsRequestSequence) {
                    return;
                }

                set({
                    shippingMethodId: id,
                    shippingSelectionStatus: 'valid',
                    staleShippingSelection: null,
                    warehouseResolutionId: admittedSelection.warehouseResolutionId,
                    warehouseResolutionVersion: admittedSelection.warehouseResolutionVersion,
                    warehouseResolutionStatus: admittedSelection.warehouseResolutionStatus,
                    warehouseResolutionExpiresAtUtc: admittedSelection.warehouseResolutionExpiresAtUtc,
                });
                get().autosaveCheckoutSession();
            },

            fetchShippingMethods: async (options = {}) => {
                const cartAtStart = useCartStore.getState().cart;
                if (!cartAtStart?.cartId) {
                    cancelShippingMethodsRequest();
                    set({
                        shippingMethodsLoading: false,
                        shippingMethodsStatus: 'IDLE',
                        shippingMethodsError: null,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                    });
                    return;
                }

                let checkoutSessionId: string | null = null;
                try {
                    checkoutSessionId = await ensureCheckoutSessionInitialized({ throwOnError: true });
                } catch (error) {
                    const classifiedFailure = classifyCheckoutMethodLoadFailure(error);
                    console.error('[CheckoutStore] Failed to initialize checkout session for shipping methods:', classifiedFailure, error);
                    set({
                        shippingMethodsLoading: false,
                        shippingMethodsStatus: classifiedFailure.status,
                        shippingMethodsError: classifiedFailure.message,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                    });
                    return;
                }

                if (!checkoutSessionId) {
                    if (!useCartStore.getState().cart?.cartId) {
                        set({
                            shippingMethodsLoading: false,
                            shippingMethodsStatus: 'IDLE',
                            shippingMethodsError: null,
                            warehouseResolutionId: null,
                            warehouseResolutionVersion: null,
                            warehouseResolutionStatus: null,
                            warehouseResolutionExpiresAtUtc: null,
                        });
                        return;
                    }

                    const error = new Error('Checkout session is required before shipping methods can be previewed.');
                    const classifiedFailure = classifyCheckoutMethodLoadFailure(error);
                    console.error('[CheckoutStore] Failed to fetch shipping methods:', classifiedFailure, error);
                    set({
                        shippingMethodsLoading: false,
                        shippingMethodsStatus: classifiedFailure.status,
                        shippingMethodsError: classifiedFailure.message,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                    });
                    return;
                }

                const currentCart = useCartStore.getState().cart;
                if (!currentCart?.cartId) {
                    cancelShippingMethodsRequest();
                    set({
                        shippingMethodsLoading: false,
                        shippingMethodsStatus: 'IDLE',
                        shippingMethodsError: null,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                    });
                    return;
                }

                const requestKey = buildShippingMethodsRequestKey(currentCart, checkoutSessionId);
                if (!options.force && shippingMethodsInFlightPromise && shippingMethodsInFlightKey === requestKey) {
                    await shippingMethodsInFlightPromise;
                    return;
                }

                if (shippingMethodsInFlightPromise) {
                    shippingMethodsAbortController?.abort();
                    shippingMethodsInFlightPromise = null;
                    shippingMethodsInFlightKey = null;
                }

                const controller = new AbortController();
                shippingMethodsAbortController = controller;
                const sequence = ++shippingMethodsRequestSequence;

                let requestPromise!: Promise<void>;
                requestPromise = (async () => {
                    set({
                        shippingMethodsLoading: true,
                        shippingMethodsStatus: 'LOADING',
                        shippingMethodsError: null,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                    });

                    try {
                        const response = await httpClient.post<unknown>(
                            CHECKOUT_ENDPOINTS.SHIPPING_METHODS_PREVIEW,
                            { checkoutSessionId },
                            {
                                signal: controller.signal,
                                headers: {
                                    'Cache-Control': 'no-store',
                                    Pragma: 'no-cache',
                                },
                            }
                        );

                        const admittedResponse = admitStorefrontShippingMethodsPreview(response.data);
                        if (controller.signal.aborted || sequence !== shippingMethodsRequestSequence) return;
                        const methods = admittedResponse.methods;

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
                            shippingMethodsStatus: admittedResponse.status,
                            shippingMethodsLoading: false,
                            shippingMethodsError: null,
                            warehouseResolutionId: null,
                            warehouseResolutionVersion: null,
                            warehouseResolutionStatus: null,
                            warehouseResolutionExpiresAtUtc: null,
                            shippingMethodId: nextShippingMethodId,
                            shippingSelectionStatus: nextShippingSelectionStatus,
                            staleShippingSelection,
                        });

                        if (nextShippingMethodId && nextShippingSelectionStatus === 'valid') {
                            await get().setShippingMethod(nextShippingMethodId, { previewSequence: sequence });
                        }

                        if (staleShippingSelection) {
                            emitCheckoutSelectionInvalidated('shipping', 'missing_from_latest_config', currentId);
                        }
                    } catch (error) {
                        if (isAutosaveAbortError(error) || controller.signal.aborted || sequence !== shippingMethodsRequestSequence) return;
                        const classifiedFailure = classifyCheckoutMethodLoadFailure(error);
                        console.error('[CheckoutStore] Failed to fetch shipping methods:', classifiedFailure, error);
                        set({
                            shippingMethodsLoading: false,
                            shippingMethodsStatus: classifiedFailure.status,
                            shippingMethodsError: classifiedFailure.message,
                            warehouseResolutionId: null,
                            warehouseResolutionVersion: null,
                            warehouseResolutionStatus: null,
                            warehouseResolutionExpiresAtUtc: null,
                        });
                    } finally {
                        if (shippingMethodsAbortController === controller) shippingMethodsAbortController = null;
                        if (shippingMethodsInFlightPromise === requestPromise) {
                            shippingMethodsInFlightPromise = null;
                            shippingMethodsInFlightKey = null;
                        }
                    }
                })();

                shippingMethodsInFlightKey = requestKey;
                shippingMethodsInFlightPromise = requestPromise;
                await requestPromise;
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
                set({ paymentMethodsLoading: true, paymentMethodsStatus: 'LOADING', paymentMethodsError: null });

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

                    const admittedResponse = admitStorefrontPaymentMethods(response.data);
                    const methods = admittedResponse.methods;
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
                        paymentMethodsStatus: admittedResponse.status,
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
                    const classifiedFailure = classifyCheckoutMethodLoadFailure(error);
                    console.error('[CheckoutStore] Failed to fetch payment methods:', classifiedFailure, error);
                    set({
                        paymentMethodsLoading: false,
                        paymentMethodsStatus: classifiedFailure.status,
                        paymentMethodsError: classifiedFailure.message,
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

            fetchCheckoutFields: async (force = false) => {
                if (!force && (get().checkoutFieldsLoading || get().checkoutFieldsStatus === 'AVAILABLE' || get().checkoutFieldsStatus === 'EMPTY')) {
                    return;
                }

                checkoutFieldsAbortController?.abort();
                const controller = new AbortController();
                checkoutFieldsAbortController = controller;
                const sequence = ++checkoutFieldsRequestSequence;
                set({ checkoutFieldsLoading: true, checkoutFieldsStatus: 'LOADING', checkoutFieldsError: null });

                try {
                    const response = await httpClient.get<CheckoutFieldDefinitionsResponse>(
                        CHECKOUT_ENDPOINTS.CHECKOUT_FIELDS,
                        {
                            signal: controller.signal,
                            headers: {
                                'Cache-Control': 'no-store',
                                Pragma: 'no-cache',
                            },
                        }
                    );

                    const admitted = admitCheckoutFields(response.data);
                    if (controller.signal.aborted || sequence !== checkoutFieldsRequestSequence) return;
                    set((state) => {
                        const customIds = new Set(admitted.fields.filter((field) => field.fieldKind === 'CUSTOM').map((field) => field.id));
                        const customFieldValues = Object.entries(state.customFieldValues).reduce<Record<string, string>>((acc, [fieldId, value]) => {
                            if (customIds.has(fieldId)) {
                                acc[fieldId] = value;
                            }
                            return acc;
                        }, {});

                        return {
                            checkoutFields: admitted.fields,
                            checkoutFieldsStatus: admitted.status,
                            checkoutFieldsLoading: false,
                            checkoutFieldsError: null,
                            checkoutFieldsTotalCount: admitted.totalCount,
                            customFieldValues,
                        };
                    });
                } catch (error) {
                    if (isAutosaveAbortError(error) || controller.signal.aborted || sequence !== checkoutFieldsRequestSequence) return;
                    const normalized = normalizeError(error as any);
                    set({
                        checkoutFields: [],
                        checkoutFieldsStatus: normalized.retryable ? 'UNAVAILABLE' : 'ERROR',
                        checkoutFieldsLoading: false,
                        checkoutFieldsError: normalized.message || 'Checkout field configuration could not be loaded.',
                        checkoutFieldsTotalCount: 0,
                    });
                } finally {
                    if (checkoutFieldsAbortController === controller) checkoutFieldsAbortController = null;
                }
            },

            setCustomFieldValue: (fieldId, value) => {
                set((state) => ({
                    customFieldValues: { ...state.customFieldValues, [fieldId]: value },
                    touchedCustomFields: new Set(state.touchedCustomFields).add(fieldId),
                }));
            },

            touchCustomField: (fieldId) => {
                set((state) => ({
                    touchedCustomFields: new Set(state.touchedCustomFields).add(fieldId),
                }));
            },

            getCustomFieldError: (fieldId) => {
                const state = get();
                if (!state.showAllErrors && !state.touchedCustomFields.has(fieldId)) {
                    return undefined;
                }

                return validateCustomCheckoutFields(getEffectiveFieldsForState(state) ?? [], state.customFieldValues)[fieldId];
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

                    // Refresh warehouse resolution and shipping methods after cart refresh.
                    await get().fetchShippingMethods({ force: true });
                    
                    // Refresh pricing after cart fetch to ensure we have up-to-date totals
                    // (fetchCart may have swapped in a different/merged cart)
                    await get().refreshPricingPreview();
                    await get().fetchCheckoutFields(true);
                    
                    const latestState = get();

                    if (!checkoutFieldsAreReady(latestState)) {
                        set({
                            isSubmitting: false,
                            error: latestState.checkoutFieldsError || 'Checkout field configuration could not be loaded.',
                            errorCode: 'CHECKOUT_FIELDS_UNAVAILABLE',
                        });
                        return false;
                    }

                    // Get fresh cart from cart store
                    const cart = useCartStore.getState().cart;
                    if (!cart?.cartId) {
                        set({ isSubmitting: false, error: 'checkout.cartEmpty' });
                        return false;
                    }

                    console.log('[CheckoutStore] Submitting checkout with cartId:', cart.cartId);

                    // Validate
                    const dynamicFieldsAvailable = latestState.checkoutFieldsTotalCount > 0;
                    const effectiveCheckoutFields = dynamicFieldsAvailable
                        ? getEffectiveCheckoutFields(latestState.checkoutFields, latestState.address, latestState.customFieldValues)
                        : [];
                    const addressFields = dynamicFieldsAvailable ? effectiveCheckoutFields : undefined;
                    const customFieldErrors = dynamicFieldsAvailable
                        ? validateCustomCheckoutFields(effectiveCheckoutFields, latestState.customFieldValues)
                        : {};

                    if (!isAddressValid(latestState.address, addressFields) || Object.keys(customFieldErrors).length > 0) {
                        set({ isSubmitting: false, error: 'checkout.addressIncomplete', showAllErrors: true });
                        return false;
                    }

                    if (!latestState.shippingMethodId) {
                        set({ isSubmitting: false, error: 'checkout.shippingMethodRequired' });
                        return false;
                    }

                    if (
                        !latestState.checkoutSessionId ||
                        !latestState.warehouseResolutionId ||
                        !latestState.warehouseResolutionVersion ||
                        latestState.warehouseResolutionStatus !== 'RESOLVED' ||
                        isWarehouseResolutionExpired(latestState.warehouseResolutionExpiresAtUtc)
                    ) {
                        set({
                            isSubmitting: false,
                            error: latestState.shippingMethodsError || 'Shipping methods must be refreshed before checkout.',
                            errorCode: isWarehouseResolutionExpired(latestState.warehouseResolutionExpiresAtUtc)
                                ? 'WAREHOUSE_RESOLUTION_EXPIRED'
                                : 'CHECKOUT_SESSION_STALE',
                        });
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
                    const visibleSystemKeys = new Set(effectiveCheckoutFields
                        .filter((field) => field.fieldKind === 'SYSTEM' && field.systemFieldKey)
                        .map((field) => field.systemFieldKey));
                    const dynamicSystemValue = (key: string, value: string | undefined) =>
                        !dynamicFieldsAvailable || visibleSystemKeys.has(key) ? (value ?? '') : '';
                    const shippingAddress = {
                        name: dynamicSystemValue('full_name', latestState.address.fullName),
                        phone: dynamicSystemValue('phone', latestState.address.phone),
                        email: dynamicSystemValue('email', latestState.address.email),
                        street: dynamicSystemValue('street_address', latestState.address.addressLine),
                        city: dynamicSystemValue('city', latestState.address.city),
                        state: dynamicSystemValue('area', latestState.address.area),
                        zipCode: dynamicSystemValue('postal_code', latestState.address.postalCode),
                        country: resolvedCountryCode,
                        geography: latestState.address.geography,
                        building: dynamicSystemValue('building', latestState.address.building),
                        floor: latestState.address.floor,
                        apartment: dynamicSystemValue('apartment', latestState.address.apartment),
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

                    const customFields = dynamicFieldsAvailable
                        ? effectiveCheckoutFields
                            .filter((field) => field.fieldKind === 'CUSTOM' && field.isEnabled)
                            .map((field) => {
                                if (isUploadCheckoutField(field.fieldType)) {
                                    const attachmentIds = parseCheckoutAttachments(latestState.customFieldValues[field.id]).map((attachment) => attachment.attachmentId);
                                    return { fieldId: field.id, attachmentIds };
                                }

                                return {
                                    fieldId: field.id,
                                    value: latestState.customFieldValues[field.id] ?? '',
                                };
                            })
                            .filter((field) => (field.attachmentIds?.length ?? 0) > 0 || field.value?.trim() || field.value === 'true' || field.value === 'false')
                        : undefined;

                    const finalRequest: CheckoutRequest = {
                        cartId: cart.cartId,
                        checkoutSessionId: latestState.checkoutSessionId,
                        warehouseResolutionId: latestState.warehouseResolutionId,
                        warehouseResolutionVersion: latestState.warehouseResolutionVersion,
                        shippingAddress,
                        shippingMethodId: latestState.shippingMethodId,
                        paymentMethod: latestState.paymentMethod,
                        couponCode: cart.appliedCoupon?.code,
                        customerNotes: dynamicSystemValue('order_notes', latestState.address.notes),
                        expectedPricing: buildExpectedPricing(latestState.pricing),
                        customFields,
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
                        checkoutSessionCartId: null,
                        warehouseResolutionId: null,
                        warehouseResolutionVersion: null,
                        warehouseResolutionStatus: null,
                        warehouseResolutionExpiresAtUtc: null,
                        customFieldValues: {},
                        touchedCustomFields: new Set(),
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
                        } else if (
                            errorCode === 'CART_CHANGED_RE_RESOLUTION_REQUIRED' ||
                            errorCode === 'WAREHOUSE_RESOLUTION_EXPIRED' ||
                            errorCode === 'CHECKOUT_SESSION_STALE' ||
                            errorCode === 'INSUFFICIENT_STOCK_AT_RESOLVED_WAREHOUSE' ||
                            errorCode === 'WAREHOUSE_INACTIVE' ||
                            errorCode === 'CARRIER_ASSIGNMENT_UNAVAILABLE' ||
                            errorCode === 'SHIPPING_METHOD_INCOMPATIBLE_WITH_WAREHOUSE'
                        ) {
                            errorMessage = axiosError.response?.data?.message ||
                                'Shipping availability changed. Please review the latest shipping methods.';
                            await get().fetchShippingMethods({ force: true });
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
                cancelCheckoutFieldsRequest();
                cancelShippingMethodsRequest();
                clearSensitiveCheckoutSessionStorage();
                set({
                    ...INITIAL_STATE,
                    touchedFields: new Set(),
                    touchedCustomFields: new Set(),
                });
            },

            // ================================================================
            // Validation
            // ================================================================

            getAddressErrors: () => {
                const state = get();
                return validateAddress(
                    state.address,
                    getEffectiveFieldsForState(state)
                );
            },

            getFieldError: (key) => {
                const state = get();
                if (key === 'notes' && state.checkoutFieldsStatus !== 'AVAILABLE') return undefined;
                if (!state.showAllErrors && !state.touchedFields.has(key)) {
                    return undefined;
                }
                const errors = validateAddress(
                    state.address,
                    getEffectiveFieldsForState(state)
                );
                // Type assertion needed because 'notes' is excluded from AddressValidationErrors
                return (errors as Record<string, string | undefined>)[key];
            },

            isStepValid: (step) => {
                const state = get();

                switch (step) {
                    case 'shipping':
                        return checkoutFieldsAreReady(state) &&
                            isAddressValid(state.address, getEffectiveFieldsForState(state)) &&
                            (state.checkoutFieldsStatus !== 'AVAILABLE' ||
                                Object.keys(validateCustomCheckoutFields(getEffectiveFieldsForState(state) ?? [], state.customFieldValues)).length === 0) &&
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
                        const baseValid = checkoutFieldsAreReady(state) &&
                            isAddressValid(state.address, getEffectiveFieldsForState(state)) &&
                            (state.checkoutFieldsStatus !== 'AVAILABLE' ||
                                Object.keys(validateCustomCheckoutFields(getEffectiveFieldsForState(state) ?? [], state.customFieldValues)).length === 0) &&
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
        customFieldValues: sensitiveCheckoutState.customFieldValues,
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
export const selectShippingMethodsStatus = (state: CheckoutState) => state.shippingMethodsStatus;
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
export const selectCheckoutFields = (state: CheckoutState) => state.checkoutFields;
export const selectCheckoutFieldsStatus = (state: CheckoutState) => state.checkoutFieldsStatus;
export const selectCheckoutFieldsLoading = (state: CheckoutState) => state.checkoutFieldsLoading;
export const selectCheckoutFieldsError = (state: CheckoutState) => state.checkoutFieldsError;
export const selectCheckoutFieldsTotalCount = (state: CheckoutState) => state.checkoutFieldsTotalCount;
export const selectCustomFieldValues = (state: CheckoutState) => state.customFieldValues;

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
export const selectPaymentMethodsStatus = (state: CheckoutState) => state.paymentMethodsStatus;
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
