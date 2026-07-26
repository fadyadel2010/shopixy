import {
  isMarketplaceOrderLifecycleStatus,
  isMarketplacePaymentStatus,
  MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES,
  MARKETPLACE_FAILED_PAYMENT_STATUSES,
  MARKETPLACE_FULFILLED_LIFECYCLES,
  MARKETPLACE_PAID_PAYMENT_STATUSES,
  MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES,
  MARKETPLACE_PAYMENT_PENDING_LIFECYCLES,
  MARKETPLACE_REFUND_PAYMENT_STATUSES
} from "@/features/marketplaceOrderAuthorityDefinitions";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function normalizeLifecycle(value: unknown): string {
  return isNonEmptyString(value) ? value.trim() : "";
}

function isPaymentPendingLifecycle(status: string) {
  return MARKETPLACE_PAYMENT_PENDING_LIFECYCLES.has(status);
}

function assertLifecyclePaymentConsistency(status: string, paymentStatus: string) {
  if (isPaymentPendingLifecycle(status) && !MARKETPLACE_PAYMENT_PENDING_PAYMENT_STATUSES.has(paymentStatus)) {
    throw new Error("Marketplace order lifecycle authority rejected payment-pending contradiction.");
  }

  if (status === "Failed" && !MARKETPLACE_FAILED_PAYMENT_STATUSES.has(paymentStatus)) {
    throw new Error("Marketplace order lifecycle authority rejected failed lifecycle contradiction.");
  }

  if (MARKETPLACE_FAILED_PAYMENT_STATUSES.has(paymentStatus) && MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES.has(status)) {
    throw new Error("Marketplace order lifecycle authority rejected failed-payment fulfillment contradiction.");
  }

  if (status === "Refunded" && !MARKETPLACE_REFUND_PAYMENT_STATUSES.has(paymentStatus)) {
    throw new Error("Marketplace order lifecycle authority rejected refund lifecycle contradiction.");
  }

  if (MARKETPLACE_FULFILLED_LIFECYCLES.has(status) && !MARKETPLACE_PAID_PAYMENT_STATUSES.has(paymentStatus) && !MARKETPLACE_REFUND_PAYMENT_STATUSES.has(paymentStatus)) {
    throw new Error("Marketplace order lifecycle authority rejected fulfilled unpaid lifecycle contradiction.");
  }
}

function assertChildLifecycleConsistency(parentStatus: string, childStatus: string, childPaymentStatus: string) {
  assertLifecyclePaymentConsistency(childStatus, childPaymentStatus);

  if ((parentStatus === "Cancelled" || parentStatus === "Failed") && MARKETPLACE_ACTIVE_FULFILLMENT_LIFECYCLES.has(childStatus)) {
    throw new Error("Marketplace order lifecycle authority rejected parent/child lifecycle contradiction.");
  }

  if (parentStatus === "Refunded" && childStatus !== "Refunded" && childStatus !== "Cancelled") {
    throw new Error("Marketplace order lifecycle authority rejected parent/child refund contradiction.");
  }
}

export function requireMarketplaceOrderLifecycleAuthority<T extends { status?: unknown; paymentStatus?: unknown; subOrders?: unknown[] }>(order: T): T {
  const status = normalizeLifecycle(order?.status);
  const paymentStatus = normalizeLifecycle(order?.paymentStatus);

  if (!isMarketplaceOrderLifecycleStatus(status)) {
    throw new Error("Marketplace order lifecycle authority rejected malformed lifecycle evidence.");
  }

  if (paymentStatus) {
    if (!isMarketplacePaymentStatus(paymentStatus)) {
      throw new Error("Marketplace order lifecycle authority rejected malformed payment lifecycle evidence.");
    }

    assertLifecyclePaymentConsistency(status, paymentStatus);
  }

  if (Array.isArray(order?.subOrders)) {
    for (const subOrder of order.subOrders as Array<Record<string, unknown>>) {
      const childStatus = normalizeLifecycle(subOrder.status);
      const childPaymentStatus = normalizeLifecycle(subOrder.paymentStatus);

      if (!isMarketplaceOrderLifecycleStatus(childStatus) || !isMarketplacePaymentStatus(childPaymentStatus)) {
        throw new Error("Marketplace order lifecycle authority rejected malformed split lifecycle evidence.");
      }

      assertChildLifecycleConsistency(status, childStatus, childPaymentStatus);
    }
  }

  if (!paymentStatus && (!Array.isArray(order?.subOrders) || order.subOrders.length === 0)) {
    throw new Error("Marketplace order lifecycle authority rejected missing payment lifecycle evidence.");
  }

  return order;
}
