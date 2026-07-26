import type { CommerceHubCheckoutResponse, CommerceHubPaymentInitiateResponse } from "@/types";
import { requireMarketplaceOrderLifecycleAuthority } from "@/features/orderLifecycleAuthority";
import { requireMarketplaceOrderFinancialAuthority, requirePaymentAmountAuthority } from "@/features/orderFinancialAuthority";
import {
  COMMERCEHUB_CHECKOUT_PAYMENT_METHODS,
  COMMERCEHUB_ONLINE_PAYMENT_METHODS,
  COMMERCEHUB_PAYMENT_HANDOFF_TYPES,
  isMarketplaceOrderLifecycleStatus,
  isMarketplacePaymentStatus
} from "@/features/marketplaceOrderAuthorityDefinitions";
import { isCommerceHubAuthorityCurrency } from "@/utils/commerceHubCurrencyAuthority";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isValidOrderStatus(value: unknown): value is string {
  return isNonEmptyString(value) && isMarketplaceOrderLifecycleStatus(value);
}

function isValidPaymentStatus(value: unknown): value is string {
  return isNonEmptyString(value) && isMarketplacePaymentStatus(value);
}

export function requireCheckoutOrderCreationAuthority(response: CommerceHubCheckoutResponse): CommerceHubCheckoutResponse {
  requireMarketplaceOrderLifecycleAuthority(response);
  requireMarketplaceOrderFinancialAuthority(response);

  if (
    !response ||
    !isNonEmptyString(response.marketplaceOrderId) ||
    !isNonEmptyString(response.orderNumber) ||
    !isCommerceHubAuthorityCurrency(response.currency) ||
    !COMMERCEHUB_CHECKOUT_PAYMENT_METHODS.has(response.paymentMethod) ||
    !isValidOrderStatus(response.status) ||
    !isValidPaymentStatus(response.paymentStatus) ||
    !isFiniteNumber(response.subtotal) ||
    !isFiniteNumber(response.shipping) ||
    !isFiniteNumber(response.shippingTotal) ||
    !isFiniteNumber(response.total) ||
    !isFiniteNumber(response.promoDiscountTotal) ||
    !isNonEmptyString(response.message) ||
    !Array.isArray(response.subOrders) ||
    response.subOrders.length === 0
  ) {
    throw new Error("Marketplace order creation authority rejected malformed checkout response evidence.");
  }

  const seenSubOrderIds = new Set<string>();
  const seenOrderItemKeys = new Set<string>();
  for (const subOrder of response.subOrders) {
    if (
      !isNonEmptyString(subOrder.subOrderId) ||
      seenSubOrderIds.has(subOrder.subOrderId) ||
      !isNonEmptyString(subOrder.orderNumber) ||
      !isNonEmptyString(subOrder.storeId) ||
      !isNonEmptyString(subOrder.storeName) ||
      !isCommerceHubAuthorityCurrency(subOrder.currency) ||
      !isFiniteNumber(subOrder.subtotal) ||
      !isFiniteNumber(subOrder.shipping) ||
      !isFiniteNumber(subOrder.total) ||
      !isValidOrderStatus(subOrder.status) ||
      !isValidPaymentStatus(subOrder.paymentStatus) ||
      !Array.isArray(subOrder.items) ||
      subOrder.items.length === 0
    ) {
      throw new Error("Marketplace order creation authority rejected malformed split-order creation evidence.");
    }

    seenSubOrderIds.add(subOrder.subOrderId);

    for (const item of subOrder.items) {
      const itemKey = `${subOrder.subOrderId}:${item.listingId}:${item.productId}:${item.variantId}`;
      if (
        !isNonEmptyString(item.listingId) ||
        !isNonEmptyString(item.productId) ||
        !isNonEmptyString(item.variantId) ||
        seenOrderItemKeys.has(itemKey) ||
        !isNonEmptyString(item.productName) ||
        !isFiniteNumber(item.quantity) ||
        item.quantity <= 0 ||
        !isFiniteNumber(item.unitPrice) ||
        !isFiniteNumber(item.total)
      ) {
        throw new Error("Marketplace order creation authority rejected malformed order item evidence.");
      }

      seenOrderItemKeys.add(itemKey);
    }
  }

  if (response.paymentSession) {
    if (
      !isNonEmptyString(response.paymentSession.provider) ||
      !isNonEmptyString(response.paymentSession.status) ||
      !isNonEmptyString(response.paymentSession.merchantReference) ||
      !isFiniteNumber(response.paymentSession.amount) ||
      !isCommerceHubAuthorityCurrency(response.paymentSession.currency)
    ) {
      throw new Error("Marketplace order creation authority rejected malformed payment-session evidence.");
    }
  }

  if (response.paymentMethod === "COD" && response.paymentSession?.status === "requires_initiation") {
    throw new Error("Marketplace order creation authority rejected COD payment-initiation contradiction.");
  }

  return response;
}

export function requirePaymentInitiationAuthority(
  response: CommerceHubPaymentInitiateResponse,
  expectedOrderId: string,
  expectedProvider: string,
  expectedAmount: number,
  expectedCurrency: string
): CommerceHubPaymentInitiateResponse {
  if (
    !response ||
    !isNonEmptyString(expectedOrderId) ||
    !isNonEmptyString(expectedProvider) ||
    !isNonEmptyString(response.paymentId) ||
    response.marketplaceOrderId !== expectedOrderId ||
    !isNonEmptyString(response.orderNumber) ||
    response.provider !== expectedProvider ||
    !COMMERCEHUB_ONLINE_PAYMENT_METHODS.has(response.provider) ||
    !isNonEmptyString(response.status) ||
    !isFiniteNumber(response.amount) ||
    !isNonEmptyString(response.currency) ||
    !COMMERCEHUB_PAYMENT_HANDOFF_TYPES.has(response.handoffType) ||
    !isNonEmptyString(response.providerReference) ||
    !isNonEmptyString(response.message)
  ) {
    throw new Error("Marketplace order creation authority rejected payment initiation evidence for the current order.");
  }

  requirePaymentAmountAuthority(response, expectedAmount, expectedCurrency);

  if (response.handoffType === "redirect_url" && !isNonEmptyString(response.paymentUrl)) {
    throw new Error("Marketplace order creation authority rejected missing payment redirect evidence.");
  }

  if (response.handoffType === "client_secret" && (!isNonEmptyString(response.paymentToken) || !isNonEmptyString(response.publishableKey))) {
    throw new Error("Marketplace order creation authority rejected missing embedded payment evidence.");
  }

  return response;
}
