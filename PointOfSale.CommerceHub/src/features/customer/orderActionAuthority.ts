import type { CustomerOrderDetails, CustomerOrderItem, CustomerOrderSummary } from "@/types";
import type { CommerceHubRecoverableOrder } from "@/types/order-recovery";
import {
  MARKETPLACE_REFUND_ITEM_STATUSES,
  MARKETPLACE_RETRY_ORDER_STATUSES,
  MARKETPLACE_RETRY_PAYMENT_STATUSES
} from "@/features/marketplaceOrderAuthorityDefinitions";

function isBoolean(value: unknown): value is boolean {
  return typeof value === "boolean";
}

function isFiniteNonNegativeNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}

function assertCustomerItemActionAuthority(item: CustomerOrderItem) {
  if (!isBoolean(item.canRefund) || !isFiniteNonNegativeNumber(item.maxRefundableQuantity)) {
    throw new Error("Customer order action authority rejected malformed refund action evidence.");
  }

  if (item.canRefund && (!MARKETPLACE_REFUND_ITEM_STATUSES.has(item.status) || item.maxRefundableQuantity <= 0)) {
    throw new Error("Customer order action authority rejected refund action contradiction.");
  }
}

function assertCustomerSplitActionAuthority(order: { subOrders?: unknown[] }) {
  for (const subOrder of order.subOrders ?? []) {
    const items = (subOrder as { items?: CustomerOrderItem[] }).items ?? [];
    for (const item of items) {
      assertCustomerItemActionAuthority(item);
    }
  }
}

export function requireCustomerOrderActionAuthority<T extends CustomerOrderSummary | CustomerOrderDetails>(order: T): T {
  assertCustomerSplitActionAuthority(order);

  if ("canRetryPayment" in order) {
    if (!isBoolean(order.canRetryPayment)) {
      throw new Error("Customer order action authority rejected malformed retry-payment action evidence.");
    }

    if (order.canRetryPayment && (!MARKETPLACE_RETRY_ORDER_STATUSES.has(order.status) || !MARKETPLACE_RETRY_PAYMENT_STATUSES.has(order.paymentStatus))) {
      throw new Error("Customer order action authority rejected retry-payment action contradiction.");
    }
  }

  return order;
}

export function requireRecoverableOrderActionAuthority<T extends CommerceHubRecoverableOrder>(order: T): T {
  if (!isBoolean(order.canRetryPayment)) {
    throw new Error("CommerceHub order recovery action authority rejected malformed retry-payment action evidence.");
  }

  if (order.canRetryPayment && (!MARKETPLACE_RETRY_ORDER_STATUSES.has(order.status) || !MARKETPLACE_RETRY_PAYMENT_STATUSES.has(order.paymentStatus))) {
    throw new Error("CommerceHub order recovery action authority rejected retry-payment action contradiction.");
  }

  return order;
}
