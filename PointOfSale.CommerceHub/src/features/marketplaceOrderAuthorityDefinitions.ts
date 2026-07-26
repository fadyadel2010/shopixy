export const MARKETPLACE_ORDER_LIFECYCLE_STATUSES: ReadonlySet<string> = new Set([
  "Pending",
  "Awaiting Payment",
  "AwaitingPayment",
  "Payment Pending",
  "PaymentPending",
  "Confirmed",
  "Processing",
  "Packed",
  "Ready For Shipment",
  "ReadyForShipment",
  "Shipped",
  "Delivered",
  "Completed",
  "Cancelled",
  "Returned",
  "Refunded",
  "Failed",
  "OnHold"
]);

export const MARKETPLACE_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Pending",
  "Authorized",
  "Collected",
  "Paid",
  "PartiallyPaid",
  "Failed",
  "Refunded",
  "PartiallyRefunded",
  "AwaitingVerification",
  "Rejected",
  "Settled",
  "CashOnDelivery",
  "Expired",
  "AmountMismatch"
]);

export const MARKETPLACE_PAID_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Authorized",
  "Collected",
  "Paid",
  "PartiallyPaid",
  "Settled",
  "CashOnDelivery"
]);

export const MARKETPLACE_FAILED_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Failed",
  "Rejected",
  "Expired",
  "AmountMismatch"
]);

export const MARKETPLACE_REFUND_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Refunded",
  "PartiallyRefunded"
]);

export const MARKETPLACE_PAYMENT_PENDING_LIFECYCLES: ReadonlySet<string> = new Set([
  "Awaiting Payment",
  "AwaitingPayment",
  "Payment Pending",
  "PaymentPending"
]);

export const MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Pending",
  "Authorized",
  "AwaitingVerification"
]);

export const MARKETPLACE_FULFILLED_LIFECYCLES: ReadonlySet<string> = new Set([
  "Delivered",
  "Completed"
]);

export const MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES: ReadonlySet<string> = new Set([
  "Processing",
  "Packed",
  "Ready For Shipment",
  "ReadyForShipment",
  "Shipped",
  "Delivered",
  "Completed"
]);

export const MARKETPLACE_RETRY_PAYMENT_STATUSES: ReadonlySet<string> = new Set([
  "Pending",
  "Failed",
  "Rejected",
  "Expired",
  "AmountMismatch"
]);

export const MARKETPLACE_RETRY_ORDER_STATUSES: ReadonlySet<string> = new Set([
  "Pending",
  "Awaiting Payment",
  "AwaitingPayment",
  "Payment Pending",
  "PaymentPending",
  "Failed"
]);

export const MARKETPLACE_REFUND_ITEM_STATUSES: ReadonlySet<string> = new Set([
  "Delivered"
]);

export const COMMERCEHUB_CHECKOUT_PAYMENT_METHODS: ReadonlySet<string> = new Set([
  "COD",
  "PAYMOB",
  "STRIPE"
]);

export const COMMERCEHUB_ONLINE_PAYMENT_METHODS: ReadonlySet<string> = new Set([
  "PAYMOB",
  "STRIPE"
]);

export const COMMERCEHUB_PAYMENT_HANDOFF_TYPES: ReadonlySet<string> = new Set([
  "redirect_url",
  "client_secret",
  "unknown"
]);

export const isMarketplaceOrderLifecycleStatus = (value: string) =>
  MARKETPLACE_ORDER_LIFECYCLE_STATUSES.has(value);

export const isMarketplacePaymentStatus = (value: string) =>
  MARKETPLACE_PAYMENT_STATUSES.has(value);
