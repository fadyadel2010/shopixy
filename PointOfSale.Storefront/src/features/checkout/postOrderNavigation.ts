export interface CheckoutSuccessRouteEvidence {
    orderId: string;
    orderNumber: string;
}

export interface PendingCheckoutRouteEvidence {
    orderId: string;
    orderNumber: string | null;
}

function normalizeRouteValue(value: unknown): string {
    return typeof value === 'string' ? value.trim() : '';
}

export function buildPendingCheckoutUrl(order: CheckoutSuccessRouteEvidence): string {
    const orderId = normalizeRouteValue(order.orderId);
    const orderNumber = normalizeRouteValue(order.orderNumber);

    if (!orderId || !orderNumber) {
        throw new Error('Checkout success response did not include canonical post-order identity.');
    }

    const params = new URLSearchParams({
        orderId,
        orderNumber,
    });

    return `/checkout/pending?${params.toString()}`;
}

export function readPendingCheckoutRouteEvidence(searchParams: URLSearchParams): PendingCheckoutRouteEvidence {
    const orderId = normalizeRouteValue(searchParams.get('orderId') || searchParams.get('order'));
    const orderNumber = normalizeRouteValue(searchParams.get('orderNumber'));

    return {
        orderId,
        orderNumber: orderNumber || null,
    };
}
