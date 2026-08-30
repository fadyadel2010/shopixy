import type { CheckoutResponse, PaymentMethod } from '@/types/checkout';

const ONLINE_PROVIDERS = new Set(['STRIPE', 'PAYMOB']);
const TERMINAL_PAYMENT_STATUSES = new Set(['completed', 'collected', 'settled', 'failed', 'cancelled', 'canceled', 'expired', 'refunded', 'rejected']);
const MANUAL_WALLET_PENDING_STATUSES = new Set(['pending', 'awaitingverification', 'awaiting_verification']);
const FAWRY_PENDING_STATUSES = new Set(['pending', 'processing']);

export interface PaymobRetrySessionResponse {
    iframeUrl: string;
    intentionId: string;
    paymentIntentId: string;
    expiresAtUtc: string;
    orderId: string;
    orderNumber: string;
    amount: number;
    currency: string;
}

function isNonEmptyString(value: unknown): value is string {
    return typeof value === 'string' && value.trim().length > 0;
}

function isFiniteNonNegative(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}

function isAbsoluteUrl(value: unknown): value is string {
    if (!isNonEmptyString(value)) {
        return false;
    }

    try {
        const url = new URL(value);
        return url.protocol === 'https:';
    } catch {
        return false;
    }
}

function isPaymobUnifiedCheckoutUrl(value: unknown): value is string {
    if (!isAbsoluteUrl(value)) {
        return false;
    }

    const url = new URL(value);
    return url.origin === 'https://accept.paymob.com' &&
        url.pathname.replace(/\/$/, '') === '/unifiedcheckout' &&
        isNonEmptyString(url.searchParams.get('publicKey')) &&
        isNonEmptyString(url.searchParams.get('clientSecret'));
}

function isFutureIsoDate(value: string): boolean {
    const parsed = Date.parse(value);
    return Number.isFinite(parsed) && parsed > Date.now();
}

export function requirePaymobRetrySessionAuthority(
    expectedOrderId: string,
    expectedAmount: number,
    expectedCurrency: string,
    response: PaymobRetrySessionResponse,
): PaymobRetrySessionResponse {
    const orderId = typeof response?.orderId === 'string' ? response.orderId.trim() : '';
    const currency = typeof response?.currency === 'string' ? response.currency.trim() : '';
    const expiresAtUtc = typeof response?.expiresAtUtc === 'string' ? response.expiresAtUtc.trim() : '';

    if (
        orderId !== expectedOrderId.trim() ||
        !isNonEmptyString(response.orderNumber) ||
        !isNonEmptyString(response.paymentIntentId) ||
        !isNonEmptyString(response.intentionId) ||
        response.amount !== expectedAmount ||
        currency !== expectedCurrency.trim() ||
        !isFutureIsoDate(expiresAtUtc) ||
        !isPaymobUnifiedCheckoutUrl(response.iframeUrl)
    ) {
        throw new Error('Backend retry response did not prove a fresh Paymob payment-session handoff.');
    }

    return { ...response, orderId, currency, expiresAtUtc };
}

/**
 * Checkout responses are provider-handoff evidence, never durable frontend
 * session authority. Reject malformed or cross-provider artifacts before they
 * can be rendered, redirected to, or copied into client state.
 */
export function requireStorefrontPaymentSessionAuthority(
    response: CheckoutResponse,
    selectedPaymentMethod: PaymentMethod,
    expectedCartId: string,
): CheckoutResponse {
    const provider = typeof selectedPaymentMethod === 'string'
        ? selectedPaymentMethod.trim().toUpperCase()
        : '';
    const total = response.totalAmount ?? response.total;
    const cartId = typeof response.cartId === 'string' ? response.cartId.trim() : '';
    const expectedCart = typeof expectedCartId === 'string' ? expectedCartId.trim() : '';
    const paymentIntentId = typeof response.paymentIntentId === 'string' ? response.paymentIntentId.trim() : '';
    const checkoutToken = typeof response.checkoutToken === 'string' ? response.checkoutToken.trim() : '';
    const expiresAtUtc = typeof response.expiresAtUtc === 'string' ? response.expiresAtUtc.trim() : '';
    const backendPaymentMethodCode = typeof response.paymentMethodCode === 'string'
        ? response.paymentMethodCode.trim().toUpperCase()
        : '';

    if (
        !response ||
        !isNonEmptyString(response.orderId) ||
        !isNonEmptyString(response.orderNumber) ||
        !isNonEmptyString(response.currency) ||
        !isFiniteNonNegative(total) ||
        !isNonEmptyString(response.status) ||
        !isNonEmptyString(response.paymentStatus) ||
        !isNonEmptyString(provider) ||
        !isNonEmptyString(backendPaymentMethodCode) ||
        backendPaymentMethodCode !== provider ||
        !isNonEmptyString(cartId) ||
        !isNonEmptyString(expectedCart) ||
        cartId !== expectedCart
    ) {
        throw new Error('Backend checkout response did not prove valid payment-session presentation evidence.');
    }

    const hasStripeArtifact = isNonEmptyString(response.stripeClientSecret) ||
        isNonEmptyString(response.stripePublishableKey);
    const hasPaymobArtifact = isNonEmptyString(response.paymobIframeUrl) ||
        isNonEmptyString(response.paymobPaymentKey);
    const hasFawryArtifact = isNonEmptyString(response.referenceCode);
    const isTerminal = TERMINAL_PAYMENT_STATUSES.has(response.paymentStatus.trim().toLowerCase());

    const providerArtifactCount = [hasStripeArtifact, hasPaymobArtifact, hasFawryArtifact].filter(Boolean).length;

    if (providerArtifactCount > 1) {
        throw new Error('Backend checkout response mixed provider payment-session artifacts.');
    }

    if (isTerminal && (hasStripeArtifact || hasPaymobArtifact || hasFawryArtifact)) {
        throw new Error('Terminal payment response unexpectedly included reusable provider payment-session artifacts.');
    }

    if (ONLINE_PROVIDERS.has(provider) && !isTerminal && !hasStripeArtifact && !hasPaymobArtifact) {
        throw new Error('Backend checkout response did not prove an online payment handoff for the selected provider.');
    }

    if (hasStripeArtifact) {
        if (
            provider !== 'STRIPE' ||
            !isNonEmptyString(paymentIntentId) ||
            !isNonEmptyString(expiresAtUtc) ||
            !isFutureIsoDate(expiresAtUtc) ||
            !isNonEmptyString(response.stripeClientSecret) ||
            !isNonEmptyString(response.stripePublishableKey)
        ) {
            throw new Error('Backend checkout response did not prove a complete Stripe handoff for the selected provider.');
        }
    }

    if (hasPaymobArtifact) {
        if (
            provider !== 'PAYMOB' ||
            !isNonEmptyString(paymentIntentId) ||
            !isNonEmptyString(expiresAtUtc) ||
            !isFutureIsoDate(expiresAtUtc) ||
            !isPaymobUnifiedCheckoutUrl(response.paymobIframeUrl)
        ) {
            throw new Error('Backend checkout response did not prove a complete Paymob handoff for the selected provider.');
        }
    }

    if (hasFawryArtifact) {
        const fawryStatus = response.paymentStatus.trim().toLowerCase();

        if (
            provider !== 'FAWRY' ||
            !isNonEmptyString(paymentIntentId) ||
            !isNonEmptyString(checkoutToken) ||
            !isNonEmptyString(expiresAtUtc) ||
            !isFutureIsoDate(expiresAtUtc) ||
            !isNonEmptyString(response.referenceCode) ||
            !FAWRY_PENDING_STATUSES.has(fawryStatus)
        ) {
            throw new Error('Backend checkout response did not prove a complete Fawry payment-session handoff.');
        }
    }

    if (!ONLINE_PROVIDERS.has(provider) && provider !== 'FAWRY' && (hasStripeArtifact || hasPaymobArtifact || hasFawryArtifact)) {
        throw new Error('Offline checkout response unexpectedly included provider payment-session artifacts.');
    }

    if (provider === 'MANUALWALLET') {
        const manualWalletStatus = response.paymentStatus.trim().toLowerCase();

        if (TERMINAL_PAYMENT_STATUSES.has(manualWalletStatus) && !isNonEmptyString(paymentIntentId)) {
            if (
                isNonEmptyString(checkoutToken) ||
                isNonEmptyString(expiresAtUtc) ||
                isNonEmptyString(response.redirectUrl) ||
                isNonEmptyString(response.paymentReference)
            ) {
                throw new Error('Terminal Manual Wallet replay unexpectedly included reusable payment-session artifacts.');
            }

            return response;
        }

        const missingManualWalletEvidence: string[] = [];
        if (!isNonEmptyString(paymentIntentId)) {
            missingManualWalletEvidence.push('paymentIntentId');
        }
        if (response.isManualWallet !== true) {
            missingManualWalletEvidence.push('isManualWallet');
        }
        if (!MANUAL_WALLET_PENDING_STATUSES.has(manualWalletStatus)) {
            missingManualWalletEvidence.push('paymentStatus');
        }
        if (!isNonEmptyString(response.walletType)) {
            missingManualWalletEvidence.push('walletType');
        }

        if (missingManualWalletEvidence.length > 0) {
            throw new Error(`Backend checkout response did not prove pending ManualWallet payment-session evidence. Missing: ${missingManualWalletEvidence.join(', ')}.`);
        }
    }

    return response;
}
