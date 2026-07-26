export const MARKETPLACE_ORDER_LIFECYCLE_STATUSES = new Set([
    'Pending',
    'Awaiting Payment',
    'AwaitingPayment',
    'Payment Pending',
    'PaymentPending',
    'Confirmed',
    'Processing',
    'Packed',
    'Ready For Shipment',
    'ReadyForShipment',
    'Shipped',
    'Delivered',
    'Completed',
    'Cancelled',
    'Returned',
    'Refunded',
    'Failed',
    'OnHold',
])

export const MARKETPLACE_PAYMENT_STATUSES = new Set([
    'Pending',
    'Authorized',
    'Collected',
    'Paid',
    'PartiallyPaid',
    'Failed',
    'Refunded',
    'PartiallyRefunded',
    'AwaitingVerification',
    'Rejected',
    'Settled',
    'CashOnDelivery',
    'Expired',
    'AmountMismatch',
])

export const MARKETPLACE_PAID_PAYMENT_STATUSES = new Set(['Authorized', 'Collected', 'Paid', 'PartiallyPaid', 'Settled', 'CashOnDelivery'])
export const MARKETPLACE_FAILED_PAYMENT_STATUSES = new Set(['Failed', 'Rejected', 'Expired', 'AmountMismatch'])
export const MARKETPLACE_REFUND_PAYMENT_STATUSES = new Set(['Refunded', 'PartiallyRefunded'])
export const MARKETPLACE_PAYMENT_PENDING_LIFECYCLES = new Set(['Awaiting Payment', 'AwaitingPayment', 'Payment Pending', 'PaymentPending'])
export const MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES = new Set(['Pending', 'Authorized', 'AwaitingVerification'])
export const MARKETPLACE_FULFILLED_LIFECYCLES = new Set(['Delivered', 'Completed'])
export const MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES = new Set(['Processing', 'Packed', 'Ready For Shipment', 'ReadyForShipment', 'Shipped', 'Delivered', 'Completed'])

export const isMarketplaceOrderLifecycleStatus = (value) => MARKETPLACE_ORDER_LIFECYCLE_STATUSES.has(value)
export const isMarketplacePaymentStatus = (value) => MARKETPLACE_PAYMENT_STATUSES.has(value)
