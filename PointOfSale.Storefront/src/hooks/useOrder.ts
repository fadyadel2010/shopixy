/**
 * useOrder Hook
 * 
 * Fetches order details and status for tracking.
 * Handles loading, error, and success states with normalized error handling.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { httpClient, normalizeError } from '@/services/api/httpClient';
import { useAuthStore, selectAuthStatus } from '@/auth/store/authStore';
import {
    persistActiveCheckoutOrderContext,
    readTrackingTokenForOrder,
} from '@/features/checkout/activeCheckoutOrderContext';
import type {
    OrderDto,
    OrderStatusDto,
    OrderError,
    OrderStatus,
    TimelineStep
} from '@/types/order';
import { AxiosError } from 'axios';
import { orderPaymentMethod } from '@/features/checkout/orderPaymentMethod';
import { buildOrderEventTimeline } from '@/features/checkout/orderEventTimeline';

// ============================================================================
// Types
// ============================================================================

export interface UseOrderResult {
    order: OrderDto | null;
    status: OrderStatusDto | null;
    timeline: TimelineStep[];
    isLoading: boolean;
    error: OrderError | null;
    refetch: () => Promise<void>;
}

export interface UseOrderOptions {
    guestTrackingToken?: string | null;
}

// ============================================================================
// Constants
// ============================================================================

/**
 * Order status progression for timeline
 */
const STATUS_PROGRESSION: OrderStatus[] = [
    'pending',
    'confirmed',
    'preparing',
    'shipped',
    'out_for_delivery',
    'delivered',
];

/**
 * Status labels and icons
 */
const STATUS_CONFIG: Record<OrderStatus, { label: string; icon: string; description: string }> = {
    pending: {
        label: 'Order Placed',
        icon: '📝',
        description: 'Your order has been received',
    },
    confirmed: {
        label: 'Confirmed',
        icon: '✓',
        description: 'Order confirmed and being processed',
    },
    preparing: {
        label: 'Preparing',
        icon: '📦',
        description: 'Your items are being prepared',
    },
    shipped: {
        label: 'Shipped',
        icon: '🚚',
        description: 'Order is on the way',
    },
    out_for_delivery: {
        label: 'Out for Delivery',
        icon: '🛵',
        description: 'Your order will arrive today',
    },
    delivered: {
        label: 'Delivered',
        icon: '🏠',
        description: 'Order has been delivered',
    },
    cancelled: {
        label: 'Cancelled',
        icon: '❌',
        description: 'Order was cancelled',
    },
    returned: {
        label: 'Returned',
        icon: '↩️',
        description: 'Shipment was returned',
    },
    failed: {
        label: 'Delivery Failed',
        icon: '⚠️',
        description: 'Delivery was unsuccessful',
    },
};

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Normalize status from backend (PascalCase) to frontend (lowercase)
 * Also handles mapping between different status naming conventions.
 */
function normalizeStatus(backendStatus: string, shippingStatus?: string): OrderStatus {
    // Convert to lowercase for comparison
    const lower = backendStatus.toLowerCase();

    // Shipping progression is independently authorized; payment/settlement must
    // never manufacture delivery. Preserve canonical order cancellation first.
    if (lower === 'cancelled' || lower === 'canceled') return 'cancelled';
    switch (shippingStatus?.toLowerCase()) {
        case 'returned': return 'returned';
        case 'failed': return 'failed';
        case 'delivered': return 'delivered';
        case 'outfordelivery':
        case 'out_for_delivery': return 'out_for_delivery';
        case 'shipped':
        case 'intransit':
        case 'pickedup': return 'shipped';
    }

    // Map backend status names to frontend OrderStatus
    switch (lower) {
        case 'pending':
            return 'pending';
        case 'confirmed':
            return 'confirmed';
        case 'processing':
            return 'preparing'; // Backend uses "Processing", frontend expects "preparing"
        case 'preparing':
            return 'preparing';
        case 'shipped':
            return 'shipped';
        case 'out_for_delivery':
        case 'outfordelivery':
            return 'out_for_delivery';
        case 'delivered':
        case 'completed':
            return 'delivered';
        case 'cancelled':
        case 'canceled':
            return 'cancelled';
        default:
            // Return as-is if it matches OrderStatus, otherwise default to 'pending'
            if (['pending', 'confirmed', 'preparing', 'shipped', 'out_for_delivery', 'delivered', 'cancelled'].includes(lower)) {
                return lower as OrderStatus;
            }
            return 'pending';
    }
}

/**
 * Parse error into normalized OrderError
 */
function parseError(error: unknown): OrderError {
    if (error instanceof AxiosError) {
        const normalized = normalizeError(error);
        const status = normalized.status;
        const code = normalized.code;

        if (normalized.isNetworkError) {
            return {
                code: 'NETWORK_ERROR',
                message: 'Unable to connect. Please check your internet connection.',
                retryable: true,
            };
        }

        if (status === 404 || code === 'ORDER_NOT_FOUND') {
            return {
                code: 'ORDER_NOT_FOUND',
                message: 'Order not found. Please check your order number.',
                retryable: false,
            };
        }

        if (status === 400 || code === 'INVALID_ORDER_ID') {
            return {
                code: 'INVALID_ORDER_ID',
                message: 'Invalid order ID format.',
                retryable: false,
            };
        }

        if (status === 403 || code === 'SESSION_MISMATCH') {
            return {
                code: 'SESSION_MISMATCH',
                message: 'You do not have access to view this order.',
                retryable: false,
            };
        }

        if (normalized.isServerError) {
            return {
                code: 'UNKNOWN_ERROR',
                message: 'Something went wrong. Please try again later.',
                retryable: true,
            };
        }
    }

    return {
        code: 'UNKNOWN_ERROR',
        message: 'An unexpected error occurred.',
        retryable: true,
    };
}

/**
 * Build timeline from order status
 */
function buildTimeline(currentStatus: OrderStatus): TimelineStep[] {
    // Terminal logistics exceptions must not show a successful delivery timeline.
    if (currentStatus === 'cancelled' || currentStatus === 'returned' || currentStatus === 'failed') {
        return [{
            id: currentStatus,
            ...STATUS_CONFIG[currentStatus],
            status: 'current',
        }];
    }

    const currentIndex = STATUS_PROGRESSION.indexOf(currentStatus);

    return STATUS_PROGRESSION.map((status, index) => {
        const config = STATUS_CONFIG[status];

        let stepStatus: 'completed' | 'current' | 'upcoming';
        if (index < currentIndex) {
            stepStatus = 'completed';
        } else if (index === currentIndex) {
            stepStatus = 'current';
        } else {
            stepStatus = 'upcoming';
        }

        return {
            id: status,
            label: config.label,
            icon: config.icon,
            description: config.description,
            status: stepStatus,
        };
    });
}

/**
 * Validate order ID format (basic GUID check)
 */
function isValidOrderId(orderId: string): boolean {
    if (!orderId || typeof orderId !== 'string') return false;
    // Basic GUID pattern check
    const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    return guidPattern.test(orderId);
}

// ============================================================================
// Hook Implementation
// ============================================================================

/**
 * Hook for fetching order details and tracking status
 */
export function useOrder(orderId: string | undefined, options: UseOrderOptions = {}): UseOrderResult {
    const [order, setOrder] = useState<OrderDto | null>(null);
    const [status, setStatus] = useState<OrderStatusDto | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<OrderError | null>(null);
    const authStatus = useAuthStore(selectAuthStatus);
    
    // Helper to get the latest tracking token at request time (not memoized)
    // This ensures renewed tokens are immediately used in subsequent requests
    const getEffectiveGuestTrackingToken = useCallback(() => {
        if (!orderId) return options.guestTrackingToken?.trim() || null;
        // Prefer persisted token (which may be renewed) over the initial prop
        return readTrackingTokenForOrder(orderId) || options.guestTrackingToken?.trim() || null;
    }, [orderId, options.guestTrackingToken]);

    const mountedRef = useRef(true);
    const requestIdRef = useRef(0);

    /**
     * Fetch order data
     */
    const fetchOrder = useCallback(async () => {
        // Validate order ID
        if (!orderId) {
            setError({
                code: 'INVALID_ORDER_ID',
                message: 'No order ID provided.',
                retryable: false,
            });
            setIsLoading(false);
            return;
        }

        if (!isValidOrderId(orderId)) {
            setError({
                code: 'INVALID_ORDER_ID',
                message: 'Invalid order ID format.',
                retryable: false,
            });
            setIsLoading(false);
            return;
        }

        if (authStatus === 'unknown') {
            setIsLoading(true);
            return;
        }

        if (authStatus === 'degraded') {
            // Increment requestId to invalidate any in-flight requests
            // This prevents stale responses from overwriting the cleared state
            ++requestIdRef.current;
            
            setError({
                code: 'NETWORK_ERROR',
                message: 'We are reconnecting your secure session. Please try again in a moment.',
                retryable: true,
            });
            setOrder(null);
            setStatus(null);
            setIsLoading(false);
            return;
        }

        const currentRequestId = ++requestIdRef.current;

        setIsLoading(true);
        setError(null);

        try {
            const isAuthenticated = authStatus === 'authenticated';
            let orderResponse;
            let detailResponse = null;

            if (isAuthenticated) {
                // Fetch tracking from the Mongo read model and enrich with refund/return details
                // from the authenticated account order detail endpoint.
                [orderResponse, detailResponse] = await Promise.all([
                    httpClient.get<any>(`/api/storefront/orders/${orderId}/track`),
                    httpClient.get<any>(`/api/storefront/account/orders/${orderId}`).catch(() => null),
                ]);
            } else {
                // For guest orders, send the tracking token from the magic link hash or persisted context.
                // This enables first-time magic-link visits without a pre-existing cookie to be validated.
                // The backend will return a renewedTrackingToken if the session is valid,
                // which we persist below for future use.
                const effectiveTrackingToken = getEffectiveGuestTrackingToken();
                const headers = effectiveTrackingToken 
                    ? { 'X-Tracking-Token': effectiveTrackingToken }
                    : undefined;
                
                orderResponse = await httpClient.get<any>(
                    `/api/storefront/orders/${orderId}/track-guest`,
                    { headers }
                );
            }

            // Only update if still mounted and latest request
            if (!mountedRef.current || currentRequestId !== requestIdRef.current) {
                return;
            }

            const orderData = orderResponse.data;
            const orderDetailData = detailResponse?.data;
            const renewedTrackingToken = orderData.renewedTrackingToken || null;

            if (!isAuthenticated && renewedTrackingToken) {
                persistActiveCheckoutOrderContext({
                    orderId,
                    orderNumber: orderData.orderNumber || orderId,
                    trackingToken: renewedTrackingToken,
                });
            }

            // Normalize status from backend (PascalCase) to frontend (lowercase)
            const normalizedStatus = normalizeStatus(orderData.status || 'pending', orderData.shippingStatus);

            // Map tracking DTO to OrderDto (adapter for UI components)
            const mappedOrder: OrderDto = {
                orderId: orderData.id,
                orderNumber: orderData.orderNumber,
                status: normalizedStatus,
                paymentStatus: orderData.paymentStatus?.toLowerCase() as any || 'pending',
                paymentMethod: orderPaymentMethod(orderData.paymentMethodCode),
                items: (orderData.items || []).map((item: any) => ({
                    productId: item.productId,
                    productName: item.productName,
                    sku: item.sku || item.SKU || '',
                    unitPrice: item.unitPrice,
                    quantity: item.quantity,
                    lineTotal: item.total,
                    imageUrl: item.imageUrl
                })),
                itemCount: orderData.items?.length || 0,
                subtotal: orderData.subtotal || 0,
                shippingCost: orderData.shippingCost || 0,
                taxAmount: orderData.taxAmount || 0,
                total: orderData.total,
                currency: orderData.currency,
                shippingAddress: orderData.shippingAddress ? {
                    fullName: orderData.customerName || '',
                    phone: orderData.shippingAddress.phone || '',
                    city: orderData.shippingAddress.city || '',
                    area: orderData.shippingAddress.state || '',
                    addressLine: orderData.shippingAddress.street || '',
                    notes: orderData.shippingAddress.street2 || undefined
                } : {
                    fullName: orderData.customerName || '',
                    phone: '',
                    city: '',
                    area: '',
                    addressLine: '',
                },
                shippingMethod: orderData.shippingMethodName ? {
                    id: '',
                    name: orderData.shippingMethodName,
                    estimatedDays: undefined
                } : undefined,
                createdAt: orderData.createdAt,
                updatedAt: orderData.createdAt, // Fallback
                shippedAt: undefined,
                deliveredAt: orderData.deliveredAt,
                trackingNumber: orderData.trackingNumber,
                carrierName: orderData.carrierName,
                cancellationReason: orderData.cancellationReason,
                refunds: (orderDetailData?.refunds || []).map((refund: any) => ({
                    refundId: refund.refundId,
                    orderItemId: refund.orderItemId,
                    productName: refund.productName,
                    variantName: refund.variantName,
                    quantity: refund.quantity,
                    amount: refund.amount,
                    currency: refund.currency || orderData.currency,
                    status: refund.status,
                    reason: refund.reason,
                    reasonCode: refund.reasonCode,
                    reasonText: refund.reasonText,
                    createdAt: refund.createdAt,
                    updatedAt: refund.updatedAt,
                    payout: refund.payout ?? null,
                    returnRequest: refund.returnRequest ?? null,
                })),
                // Attach server-side timeline for the hook to use
                timeline: orderData.timeline
            } as any;

            setOrder(mappedOrder);

            // Build status from order data
            const statusData: OrderStatusDto = {
                orderId: orderData.id,
                orderNumber: orderData.orderNumber,
                status: normalizedStatus,
                statusText: STATUS_CONFIG[normalizedStatus]?.description || 'Order is being processed',
                trackingNumber: orderData.trackingNumber,
                carrierName: orderData.carrierName,
                lastUpdated: orderData.createdAt, // Use createdAt as lastUpdated fallback
                estimatedDelivery: orderData.estimatedDeliveryDate
            };
            setStatus(statusData);
            setError(null);

        } catch (err) {
            console.error('[useOrder] Failed to fetch order:', err);

            if (!mountedRef.current || currentRequestId !== requestIdRef.current) {
                return;
            }

            setError(parseError(err));
            setOrder(null);
            setStatus(null);
        } finally {
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [authStatus, getEffectiveGuestTrackingToken, orderId]);

    /**
     * Build timeline from current status
     */
    const timeline = useMemo(() => {
        if (!order) return [];

        // Phase 10.5.A: Prefer server-side timeline if available
        const serverTimeline = order.timeline;
        if (serverTimeline && Array.isArray(serverTimeline) && serverTimeline.length > 0) {
            return buildOrderEventTimeline(serverTimeline);
        }

        // Fallback to client-side generated timeline
        return buildTimeline(order.status);
    }, [order]);

    /**
     * Effect: Fetch on mount and orderId change
     */
    useEffect(() => {
        mountedRef.current = true;
        fetchOrder();

        return () => {
            mountedRef.current = false;
        };
    }, [fetchOrder]);

    useEffect(() => {
        const hasActiveReturn = order?.refunds?.some((refund) => {
            const returnStatus = refund.returnRequest?.status;
            return returnStatus && returnStatus !== 'Confirmed' && returnStatus !== 'Failed';
        });

        if (!hasActiveReturn) {
            return;
        }

        const intervalId = window.setInterval(() => {
            void fetchOrder();
        }, 15000);

        return () => {
            window.clearInterval(intervalId);
        };
    }, [order, fetchOrder]);

    /**
     * Refetch function
     */
    const refetch = useCallback(async () => {
        await fetchOrder();
    }, [fetchOrder]);

    return {
        order,
        status,
        timeline,
        isLoading,
        error,
        refetch,
    };
}

/**
 * Export status config for use in components
 */
export { STATUS_CONFIG, STATUS_PROGRESSION };
