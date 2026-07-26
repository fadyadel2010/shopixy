import type { CheckoutResponse, PaymentMethod } from '@/types/checkout';

const ONLINE_PROVIDERS = new Set(['STRIPE', 'PAYMOB']);
const TERMINAL_PAYMENT_STATUSES = new Set(['completed', 'failed', 'cancelled', 'canceled', 'expired', 'refunded', 'rejected']);
const MANUAL_WALLET_PENDING_STATUSES = new Set(['pending', 'awaitingverification', 'awaiting_verification']);
const FAWRY_PENDING_STATUSES = new Set(['pending', 'processing']);

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

function isFutureIsoDate(value: string): boolean {
    const parsed = Date.parse(value);
    return Number.isFinite(parsed) && parsed > Date.now();
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

    if (
        !response ||
        !isNonEmptyString(response.orderId) ||
        !isNonEmptyString(response.orderNumber) ||
        !isNonEmptyString(response.currency) ||
        !isFiniteNonNegative(total) ||
        !isNonEmptyString(response.status) ||
        !isNonEmptyString(response.paymentStatus) ||
        !isNonEmptyString(provider) ||
        !isNonEmptyString(cartId) ||
        !isNonEmptyString(expectedCart) ||
        cartId !== expectedCart
    ) {
        throw new Error('Backend checkout response did not prove valid payment-session presentation evidence.');
    }

    const hasStripeArtifact = response.stripeClientSecret !== undefined || response.stripePublishableKey !== undefined;
    const hasPaymobArtifact = response.paymobIframeUrl !== undefined || response.paymobPaymentKey !== undefined;
    const hasFawryArtifact = response.referenceCode !== undefined;
    const isTerminal = TERMINAL_PAYMENT_STATUSES.has(response.paymentStatus.trim().toLowerCase());

    const providerArtifactCount = [hasStripeArtifact, hasPaymobArtifact, hasFawryArtifact].filter(Boolean).length;

    if (providerArtifactCount > 1) {
        throw new Error('Backend checkout response mixed provider payment-session artifacts.');
    }

    if (isTerminal && (hasStripeArtifact || hasPaymobArtifact || hasFawryArtifact)) {
        throw new Error('Terminal payment response unexpectedly included reusable provider payment-session artifacts.');
    }

    if (hasStripeArtifact) {
        if (
            provider !== 'STRIPE' ||
            !isNonEmptyString(paymentIntentId) ||
            !isNonEmptyString(checkoutToken) ||
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
            !isNonEmptyString(checkoutToken) ||
            !isNonEmptyString(expiresAtUtc) ||
            !isFutureIsoDate(expiresAtUtc) ||
            !isAbsoluteUrl(response.paymobIframeUrl) ||
            !isNonEmptyString(response.paymobPaymentKey)
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

        if (
            !isNonEmptyString(paymentIntentId) ||
            response.isManualWallet !== true ||
            !MANUAL_WALLET_PENDING_STATUSES.has(manualWalletStatus) ||
            !isNonEmptyString(response.merchantWalletNumber) ||
            !isNonEmptyString(response.walletType) ||
            !isNonEmptyString(response.walletInstructions)
        ) {
            throw new Error('Backend checkout response did not prove pending ManualWallet payment-session evidence.');
        }
    }

    return response;
}
