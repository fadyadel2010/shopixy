import { httpClient } from './httpClient';
import {
    requireCanonicalPaymentStatusForOrder,
    type CanonicalPaymentStatusResponse,
} from '@/features/checkout/paymentFlow';
import {
    requirePaymobRetrySessionAuthority,
    type PaymobRetrySessionResponse,
} from '@/features/checkout/paymentSessionAuthority';

const PAYMENT_STATUS_ENDPOINTS = {
    GET_STATUS: (orderId: string) => `/api/storefront/payments/status/${encodeURIComponent(orderId)}`,
    RETRY_PAYMOB: (orderId: string) => `/api/storefront/payments/paymob/intent/${encodeURIComponent(orderId)}`,
} as const;

export interface CanonicalPaymentStatusRequestOptions {
    signal?: AbortSignal;
    checkoutToken?: string | null;
}

export async function getCanonicalPaymentStatus(
    orderId: string,
    options: CanonicalPaymentStatusRequestOptions = {},
): Promise<CanonicalPaymentStatusResponse> {
    const response = await httpClient.get<CanonicalPaymentStatusResponse>(
        PAYMENT_STATUS_ENDPOINTS.GET_STATUS(orderId),
        {
            signal: options.signal,
            headers: options.checkoutToken
                ? { 'X-Checkout-Token': options.checkoutToken }
                : undefined,
        }
    );

    return requireCanonicalPaymentStatusForOrder(orderId, response.data);
}

export async function retryPaymobPayment(
    status: CanonicalPaymentStatusResponse,
    options: CanonicalPaymentStatusRequestOptions = {},
): Promise<PaymobRetrySessionResponse> {
    if (!status.canRetryPayment) {
        throw new Error('Backend payment status did not authorize retry.');
    }

    const response = await httpClient.post<PaymobRetrySessionResponse>(
        PAYMENT_STATUS_ENDPOINTS.RETRY_PAYMOB(status.orderId),
        undefined,
        {
            signal: options.signal,
            headers: options.checkoutToken
                ? { 'X-Checkout-Token': options.checkoutToken }
                : undefined,
        }
    );

    return requirePaymobRetrySessionAuthority(
        status.orderId,
        status.totalAmount,
        status.currency,
        response.data,
    );
}

export const paymentStatusApi = {
    getCanonicalPaymentStatus,
    retryPaymobPayment,
};
