import type { CustomerOrderDetails, CustomerOrderItemExistingRefund, CustomerOrderSummary, CustomerOrdersResponse } from "@/types";
import type { CommerceHubRecoverableOrder } from "@/types/order-recovery";
import { isMarketplaceOrderLifecycleStatus, isMarketplacePaymentStatus } from "@/features/marketplaceOrderAuthorityDefinitions";
import { requireMarketplaceOrderLifecycleAuthority } from "@/features/orderLifecycleAuthority";
import { requireMarketplaceOrderFinancialAuthority } from "@/features/orderFinancialAuthority";
import { isCommerceHubAuthorityCurrency } from "@/utils/commerceHubCurrencyAuthority";
import { requireCustomerOrderActionAuthority, requireRecoverableOrderActionAuthority } from "./orderActionAuthority";
import { requireCustomerExistingRefundVisibilityAuthority } from "./paymentVisibilityAuthority";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isValidStatus(value: unknown): value is string {
  return isNonEmptyString(value) && isMarketplaceOrderLifecycleStatus(value);
}

function isValidPaymentStatus(value: unknown): value is string {
  return isNonEmptyString(value) && isMarketplacePaymentStatus(value);
}

function assertCustomerOrderItems(items: unknown[]) {
  const seenOrderItemIds = new Set<string>();
  for (const item of items as Array<Record<string, unknown>>) {
    if (
      !isNonEmptyString(item.orderItemId) ||
      seenOrderItemIds.has(item.orderItemId) ||
      !isNonEmptyString(item.productName) ||
      !isFiniteNumber(item.quantity) ||
      item.quantity <= 0 ||
      !isFiniteNumber(item.price) ||
      !isFiniteNumber(item.total)
    ) {
      throw new Error("Customer order identity authority rejected malformed split order item evidence.");
    }

    seenOrderItemIds.add(item.orderItemId);
    if (!Array.isArray(item.existingRefunds)) {
      throw new Error("Customer payment visibility authority rejected malformed embedded refund evidence.");
    }

    const seenRefundIds = new Set<string>();
    for (const refund of item.existingRefunds as CustomerOrderItemExistingRefund[]) {
      const acceptedRefund = requireCustomerExistingRefundVisibilityAuthority(refund, item.total as number);
      if (seenRefundIds.has(acceptedRefund.refundId)) {
        throw new Error("Customer payment visibility authority rejected duplicate embedded refund evidence.");
      }

      seenRefundIds.add(acceptedRefund.refundId);
    }
  }
}

function assertCustomerSubOrders(order: { subOrders?: unknown[]; currency?: unknown }) {
  if (!Array.isArray(order.subOrders) || order.subOrders.length === 0 || !isCommerceHubAuthorityCurrency(typeof order.currency === "string" ? order.currency : null)) {
    throw new Error("Customer order identity authority rejected malformed split order evidence.");
  }

  const seenSubOrderIds = new Set<string>();
  for (const subOrder of order.subOrders as Array<Record<string, unknown>>) {
    if (
      !isNonEmptyString(subOrder.subOrderId) ||
      seenSubOrderIds.has(subOrder.subOrderId) ||
      !isNonEmptyString(subOrder.orderNumber) ||
      !isNonEmptyString(subOrder.vendorName) ||
      !isValidStatus(subOrder.status) ||
      !isValidPaymentStatus(subOrder.paymentStatus) ||
      !isFiniteNumber(subOrder.totalAmount) ||
      !isFiniteNumber(subOrder.shippingAmount) ||
      !Array.isArray(subOrder.items) ||
      subOrder.items.length === 0
    ) {
      throw new Error("Customer order identity authority rejected malformed split order evidence.");
    }

    seenSubOrderIds.add(subOrder.subOrderId);
    assertCustomerOrderItems(subOrder.items);
  }
}

function assertCustomerOrderSummary(order: CustomerOrderSummary) {
  requireMarketplaceOrderLifecycleAuthority(order);
  requireMarketplaceOrderFinancialAuthority(order);

  if (
    !isNonEmptyString(order.orderId) ||
    !isNonEmptyString(order.orderNumber) ||
    !isNonEmptyString(order.createdAt) ||
    !isFiniteNumber(order.totalAmount) ||
    !isValidStatus(order.status)
  ) {
    throw new Error("Customer order identity authority rejected malformed order summary evidence.");
  }

  assertCustomerSubOrders(order);
  requireCustomerOrderActionAuthority(order);
}

export function requireCustomerOrdersListAuthority(response: CustomerOrdersResponse): CustomerOrdersResponse {
  if (!response || !Array.isArray(response.items)) {
    throw new Error("Customer order identity authority rejected malformed order list evidence.");
  }

  const seenOrderIds = new Set<string>();
  for (const order of response.items) {
    assertCustomerOrderSummary(order);
    if (seenOrderIds.has(order.orderId)) {
      throw new Error("Customer order identity authority rejected duplicate order identity evidence.");
    }

    seenOrderIds.add(order.orderId);
  }

  return response;
}

export function requireCustomerOrderDetailAuthority(order: CustomerOrderDetails, expectedOrderId: string): CustomerOrderDetails {
  requireMarketplaceOrderLifecycleAuthority(order);
  requireMarketplaceOrderFinancialAuthority(order);

  if (
    !isNonEmptyString(expectedOrderId) ||
    !isNonEmptyString(order?.orderId) ||
    order.orderId !== expectedOrderId ||
    !isNonEmptyString(order.orderNumber) ||
    !isNonEmptyString(order.createdAt) ||
    !isFiniteNumber(order.totalAmount) ||
    !isValidStatus(order.status) ||
    !isValidPaymentStatus(order.paymentStatus)
  ) {
    throw new Error("Customer order identity authority rejected detail evidence for the current route.");
  }

  assertCustomerSubOrders(order);
  return requireCustomerOrderActionAuthority(order);
}

export function requireRecoverableOrderAuthority(order: CommerceHubRecoverableOrder, expectedOrderId: string): CommerceHubRecoverableOrder {
  requireMarketplaceOrderLifecycleAuthority(order);
  requireMarketplaceOrderFinancialAuthority(order);

  if (
    !isNonEmptyString(expectedOrderId) ||
    !isNonEmptyString(order?.marketplaceOrderId) ||
    order.marketplaceOrderId !== expectedOrderId ||
    !isNonEmptyString(order.orderNumber) ||
    !isFiniteNumber(order.total) ||
    !isValidStatus(order.status) ||
    !isValidPaymentStatus(order.paymentStatus)
  ) {
    throw new Error("CommerceHub order recovery authority rejected detail evidence for the current route.");
  }

  if (!Array.isArray(order.subOrders) || order.subOrders.length === 0) {
    throw new Error("CommerceHub order recovery authority rejected malformed split order evidence.");
  }

  const seenSubOrderIds = new Set<string>();
  for (const subOrder of order.subOrders) {
    if (
      !isNonEmptyString(subOrder.subOrderId) ||
      seenSubOrderIds.has(subOrder.subOrderId) ||
      !isNonEmptyString(subOrder.orderNumber) ||
      !isNonEmptyString(subOrder.storeId) ||
      !isNonEmptyString(subOrder.storeName) ||
      !isCommerceHubAuthorityCurrency(typeof subOrder.currency === "string" ? subOrder.currency : null) ||
      !isFiniteNumber(subOrder.subtotal) ||
      !isFiniteNumber(subOrder.shipping) ||
      !isFiniteNumber(subOrder.total) ||
      !isValidStatus(subOrder.status) ||
      !isValidPaymentStatus(subOrder.paymentStatus) ||
      !Array.isArray(subOrder.items) ||
      subOrder.items.length === 0
    ) {
      throw new Error("CommerceHub order recovery authority rejected malformed split order evidence.");
    }

    seenSubOrderIds.add(subOrder.subOrderId);

    const seenItemKeys = new Set<string>();
    for (const item of subOrder.items as Array<Record<string, unknown>>) {
      const itemKey = `${item.listingId}:${item.productId}:${item.variantId}`;
      if (
        !isNonEmptyString(item.listingId) ||
        !isNonEmptyString(item.productId) ||
        !isNonEmptyString(item.variantId) ||
        seenItemKeys.has(itemKey) ||
        !isNonEmptyString(item.productName) ||
        !isFiniteNumber(item.quantity) ||
        item.quantity <= 0 ||
        !isFiniteNumber(item.unitPrice) ||
        !isFiniteNumber(item.total)
      ) {
        throw new Error("CommerceHub order recovery authority rejected malformed split order item evidence.");
      }

      seenItemKeys.add(itemKey);
    }
  }

  return requireRecoverableOrderActionAuthority(order);
}
