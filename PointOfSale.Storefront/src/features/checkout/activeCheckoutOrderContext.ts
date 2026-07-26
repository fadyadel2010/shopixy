export interface ActiveCheckoutOrderContext {
    orderId: string;
    orderNumber: string;
    createdAt: string;
    checkoutToken?: string | null;
    trackingToken?: string | null;
}

interface TrackingTokenUrlMitigationResult {
    removed: boolean;
    orderId: string | null;
    tokenSource: 'query' | 'fragment' | null;
}

export function readActiveCheckoutOrderContext(): ActiveCheckoutOrderContext | null {
    // Payment/order identity is backend-owned and can never be restored from
    // browser storage, a route, or prior in-memory checkout state.
    return null;
}

export function persistActiveCheckoutOrderContext(order: {
    orderId: string;
    orderNumber: string;
    checkoutToken?: string | null;
    trackingToken?: string | null;
}): void {
    // Deliberately no-op: mutation data cannot establish persistent payment
    // identity authority. The next presentation must re-enter the backend.
    void order;
}

export function clearActiveCheckoutOrderContext(): void {
    // Compatibility boundary: no frontend identity state exists to clear.
}

export function clearActiveCheckoutOrderContextIfMatches(orderId: string): void {
    const activeOrder = readActiveCheckoutOrderContext();
    if (!activeOrder || activeOrder.orderId !== orderId) {
        return;
    }

    clearActiveCheckoutOrderContext();
}

export function clearCheckoutTokenForOrder(orderId: string): void {
    void orderId;
}

export function readCheckoutTokenForOrder(orderId: string): string | null {
    void orderId;
    return null;
}

export function readTrackingTokenForOrder(orderId: string): string | null {
    void orderId;
    return null;
}

export function storeTrackingTokenForOrder(order: {
    orderId: string;
    orderNumber?: string | null;
    trackingToken: string;
}): void {
    void order;
}

export function removeTrackingTokenFromCurrentUrl(): TrackingTokenUrlMitigationResult {
    if (typeof window === 'undefined') {
        return { removed: false, orderId: null, tokenSource: null };
    }

    let url: URL;
    try {
        url = new URL(window.location.href);
    } catch {
        return { removed: false, orderId: null, tokenSource: null };
    }

    const tokenFromQuery = url.searchParams.get('token') || url.searchParams.get('guestToken');
    const fragmentParams = parseFragmentParams(url.hash);
    const tokenFromFragment = fragmentParams.get('tracking_token') ||
        fragmentParams.get('guestToken') ||
        fragmentParams.get('token');
    const trackingToken = tokenFromQuery || tokenFromFragment;
    const orderId = extractTrackingOrderId(url.pathname);
    const hadTokenQuery = url.searchParams.has('token') || url.searchParams.has('guestToken');
    const hadTokenFragment = fragmentParams.has('tracking_token') ||
        fragmentParams.has('guestToken') ||
        fragmentParams.has('token');

    if (!hadTokenQuery && !hadTokenFragment) {
        return { removed: false, orderId, tokenSource: null };
    }

    if (orderId && trackingToken?.trim()) {
        storeTrackingTokenForOrder({
            orderId,
            orderNumber: orderId,
            trackingToken: trackingToken.trim(),
        });
    }

    url.searchParams.delete('token');
    url.searchParams.delete('guestToken');
    const sanitizedHash = removeTokenFragmentParams(url.hash);
    const sanitizedPath = `${url.pathname}${url.search}${sanitizedHash}`;
    window.history.replaceState(window.history.state, document.title, sanitizedPath);

    const tokenSource = hadTokenQuery ? 'query' : 'fragment';
    emitTrackingTokenRemovedMetric(tokenSource);

    return { removed: true, orderId, tokenSource };
}

function extractTrackingOrderId(pathname: string): string | null {
    const match = pathname.match(/\/orders\/([^/]+)\/track\/?$/i);
    return match?.[1] ? decodeURIComponent(match[1]) : null;
}

function parseFragmentParams(hash: string): URLSearchParams {
    const fragment = hash.startsWith('#') ? hash.slice(1) : hash;
    const queryStart = fragment.startsWith('?') ? fragment.slice(1) : fragment;
    return new URLSearchParams(queryStart);
}

function removeTokenFragmentParams(hash: string): string {
    if (!hash) {
        return '';
    }

    const params = parseFragmentParams(hash);
    params.delete('tracking_token');
    params.delete('guestToken');
    params.delete('token');
    const serialized = params.toString();
    return serialized ? `#${serialized}` : '';
}

function emitTrackingTokenRemovedMetric(source: 'query' | 'fragment'): void {
    void import('@/features/checkout/checkoutTelemetry')
        .then(({ emitTrackingTokenRemovedFromUrl }) => {
            emitTrackingTokenRemovedFromUrl(source);
        })
        .catch(() => {
            // Best-effort security telemetry only.
        });
}
