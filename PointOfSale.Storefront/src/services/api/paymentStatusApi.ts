import { httpClient } from './httpClient';
import {
    requireCanonicalPaymentStatusForOrder,
    type CanonicalPaymentStatusResponse,
} from '@/features/checkout/paymentFlow';

const PAYMENT_STATUS_ENDPOINTS = {
    GET_STATUS: (orderId: string) => `/api/payments/status/${encodeURIComponent(orderId)}`,
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

export const paymentStatusApi = {
    getCanonicalPaymentStatus,
};
