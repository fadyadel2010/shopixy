/**
 * Order Types
 * 
 * Type definitions for order data structures.
 * Matches backend Order API responses.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

// ============================================================================
// Order Status
// ============================================================================

/**
 * Order status enum
 * Represents the current stage of order processing
 */
export type OrderStatus =
    | 'pending'
    | 'confirmed'
    | 'preparing'
    | 'shipped'
    | 'out_for_delivery'
    | 'delivered'
    | 'cancelled';

/**
 * Payment status enum
 */
export type PaymentStatus = 'pending' | 'completed' | 'failed' | 'refunded';
export type RefundStatus =
    | 'Requested'
    | 'UnderReview'
    | 'Approved'
    | 'WaitingForReturn'
    | 'AwaitingPayout'
    | 'ReadyForExecution'
    | 'Rejected'
    | 'Cancelled'
    | 'Processing'
    | 'Paid'
    | 'Executed'
    | 'Completed'
    | 'Failed';
export type RefundExecutionTrigger = 'OnPickup' | 'OnDelivery' | 'OnVendorConfirmation';
export type ReturnStatus =
    | 'Pending'
    | 'PickupScheduled'
    | 'PickedUp'
    | 'InTransit'
    | 'DeliveredToVendor'
    | 'Confirmed'
    | 'Failed';

// ============================================================================
// Order Item
// ============================================================================

/**
 * Order item in an order
 */
export interface OrderItemDto {
    productId: string;
    productName: string;
    productNameAr?: string;
    sku: string;
    unitPrice: number;
    quantity: number;
    lineTotal: number;
    imageUrl?: string;
}

// ============================================================================
// Shipping Address
// ============================================================================

/**
 * Shipping address on an order
 */
export interface OrderShippingAddress {
    fullName: string;
    phone: string;
    city: string;
    area: string;
    addressLine: string;
    notes?: string;
}

// ============================================================================
// Order DTO
// ============================================================================

/**
 * Full order response
 */
export interface OrderDto {
    orderId: string;
    orderNumber: string;
    status: OrderStatus;
    paymentStatus: PaymentStatus;
    paymentMethod: 'cod' | 'online';

    // Items
    items: OrderItemDto[];
    itemCount: number;

    // Pricing
    subtotal: number;
    shippingCost: number;
    discountAmount: number;
    /** PHASE: Promotions Engine - Promotion discount (separate from coupon) */
    promotionDiscountAmount?: number;
    taxAmount: number;
    total: number;
    currency: string;
    couponCode?: string;

    // Shipping
    shippingAddress: OrderShippingAddress;
    shippingMethod?: {
        id: string;
        name: string;
        estimatedDays?: number;
    };

    // Dates
    createdAt: string;
    updatedAt?: string;
    shippedAt?: string;
    deliveredAt?: string;

    // Tracking
    trackingNumber?: string;
    carrierName?: string;
    cancellationReason?: string;
    refunds?: OrderRefundDto[];
}

// ============================================================================
// Order Status Response
// ============================================================================

/**
 * Order status response for tracking
 */
export interface OrderStatusDto {
    orderId: string;
    orderNumber: string;
    status: OrderStatus;
    statusText: string;
    estimatedDelivery?: string;
    trackingNumber?: string;
    carrierName?: string;
    lastUpdated: string;
}

// ============================================================================
// Timeline Step
// ============================================================================

/**
 * Timeline step for visual order tracking
 */
export interface TimelineStep {
    id: OrderStatus;
    label: string;
    icon: string;
    status: 'completed' | 'current' | 'upcoming';
    timestamp?: string;
    description?: string;
}

// ============================================================================
// Order Error Types
// ============================================================================

export type OrderErrorCode =
    | 'ORDER_NOT_FOUND'
    | 'INVALID_ORDER_ID'
    | 'SESSION_MISMATCH'
    | 'NETWORK_ERROR'
    | 'UNKNOWN_ERROR';

export interface OrderError {
    code: OrderErrorCode;
    message: string;
    retryable: boolean;
}

// ============================================================================
// Customer Order History (Phase F2.Orders)
// ============================================================================

/**
 * Customer order item for order history
 */
export interface CustomerOrderItem {
    productId?: string;
    productName: string;
    sku?: string;
    quantity: number;
    price: number;
    total?: number;
    imageUrl?: string;
}

/**
 * Customer order list item (for order history list)
 */
export interface CustomerOrder {
    id: string;
    orderNumber: string;
    status: string;
    paymentStatus: string;
    total: number;
    currency: string;
    itemCount: number;
    shippingMethodName?: string;
    paymentMethodName?: string;
    createdAt: string;
    items: CustomerOrderItem[];
}

/**
 * Customer order detail (for single order view)
 */
export interface CustomerOrderDetail extends CustomerOrder {
    subtotal: number;
    shippingCost: number;
    discountAmount: number;
    taxAmount: number;
    couponCode?: string;
    customerNotes?: string;
    shippingAddress: string;
    shippingCity?: string;
    shippingState?: string;
    shippingZipCode?: string;
    shippingCountry?: string;
    refunds?: OrderRefundDto[];
}

export interface ReturnRequestDto {
    returnId: string;
    refundId: string;
    orderItemId: string;
    customerId: string;
    vendorId: string;
    status: ReturnStatus;
    refundExecutionTrigger: RefundExecutionTrigger;
    pickupScheduledAt?: string | null;
    pickedUpAt?: string | null;
    inTransitAt?: string | null;
    deliveredAt?: string | null;
    confirmedAt?: string | null;
    trackingNumber?: string | null;
    courierName?: string | null;
    notes?: string | null;
    createdAt: string;
    updatedAt?: string | null;
    canSchedulePickup: boolean;
    canMarkPickedUp: boolean;
    canMarkInTransit: boolean;
    canMarkDelivered: boolean;
    canMarkFailed: boolean;
    canConfirmReceipt: boolean;
    isExecutionTriggerReached: boolean;
}

export interface RefundPayoutSnapshotDto {
    method: 'VodafoneCash' | 'InstaPay' | 'BankTransfer';
    maskedAccountIdentifier: string;
    accountHolderName?: string | null;
    transactionReference?: string | null;
    paidAt?: string | null;
}

export interface OrderRefundDto {
    refundId: string;
    orderItemId: string;
    productName: string;
    variantName?: string | null;
    quantity: number;
    amount: number;
    currency: string;
    status: RefundStatus;
    reason: string;
    reasonCode: string;
    reasonText?: string | null;
    createdAt: string;
    updatedAt?: string | null;
    payout?: RefundPayoutSnapshotDto | null;
    returnRequest?: ReturnRequestDto | null;
}

/**
 * Paginated customer orders response
 */
export interface CustomerOrdersResponse {
    orders: CustomerOrder[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

/**
 * Order status configuration for UI display
 */
export const ORDER_STATUS_CONFIG: Record<string, { color: string; icon: string; bgColor: string }> = {
    Pending: { color: 'text-amber-600', bgColor: 'bg-amber-50', icon: '⏳' },
    Confirmed: { color: 'text-blue-600', bgColor: 'bg-blue-50', icon: '✓' },
    Processing: { color: 'text-purple-600', bgColor: 'bg-purple-50', icon: '📦' },
    Shipped: { color: 'text-cyan-600', bgColor: 'bg-cyan-50', icon: '🚚' },
    Delivered: { color: 'text-green-600', bgColor: 'bg-green-50', icon: '✅' },
    Cancelled: { color: 'text-red-600', bgColor: 'bg-red-50', icon: '✕' },
    Refunded: { color: 'text-gray-600', bgColor: 'bg-gray-50', icon: '↩️' },
};

export const PAYMENT_STATUS_CONFIG: Record<string, { color: string; bgColor: string }> = {
    Pending: { color: 'text-amber-600', bgColor: 'bg-amber-50' },
    Paid: { color: 'text-green-600', bgColor: 'bg-green-50' },
    Failed: { color: 'text-red-600', bgColor: 'bg-red-50' },
    Refunded: { color: 'text-gray-600', bgColor: 'bg-gray-50' },
    PartiallyRefunded: { color: 'text-orange-600', bgColor: 'bg-orange-50' },
};

