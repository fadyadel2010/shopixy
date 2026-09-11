import {
  COMMERCEHUB_AUTHORITY_CURRENCY,
  isCommerceHubAuthorityCurrency
} from "@/utils/commerceHubCurrencyAuthority";

/**
 * Frontend financial boundary: values are accepted only as a coherent backend
 * order snapshot. This validates backend evidence; it never calculates or
 * repairs a customer, vendor, settlement, commission, or refund amount.
 */
const MONEY_EPSILON = 0.005;

function isFiniteNonNegativeMoney(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}

function sameMoney(left: number, right: number) {
  return Math.abs(left - right) < MONEY_EPSILON;
}

function requireMoney(value: unknown, message: string): number {
  if (!isFiniteNonNegativeMoney(value)) throw new Error(message);
  return value;
}

function requireCommerceHubCurrency(value: unknown, message: string): string {
  if (typeof value !== "string" || !isCommerceHubAuthorityCurrency(value)) {
    throw new Error(message);
  }

  return COMMERCEHUB_AUTHORITY_CURRENCY;
}

function assertBreakdown(total: unknown, subtotal: unknown, shipping: unknown, message: string) {
  const acceptedTotal = requireMoney(total, message);
  const acceptedSubtotal = requireMoney(subtotal, message);
  const acceptedShipping = requireMoney(shipping, message);
  if (!sameMoney(acceptedTotal, acceptedSubtotal + acceptedShipping)) throw new Error(message);
}

export function requireMarketplaceOrderFinancialAuthority<T extends Record<string, unknown>>(order: T): T {
  requireCommerceHubCurrency(order.currency, "Marketplace order financial authority rejected non-EGP currency evidence.");
  const total = order.totalAmount ?? order.total;
  const subtotal = order.subtotal;
  const shipping = order.shippingTotal ?? order.shipping;
  if (subtotal !== undefined || shipping !== undefined) {
    assertBreakdown(total, subtotal, shipping, "Marketplace order financial authority rejected contradictory order totals.");
  } else {
    requireMoney(total, "Marketplace order financial authority rejected malformed order total.");
  }

  if (!Array.isArray(order.subOrders) || order.subOrders.length === 0) {
    throw new Error("Marketplace order financial authority rejected missing split financial evidence.");
  }

  let subOrderTotal = 0;
  for (const subOrder of order.subOrders as Array<Record<string, unknown>>) {
    if ("currency" in subOrder) {
      requireCommerceHubCurrency(subOrder.currency, "Marketplace order financial authority rejected mixed-currency split evidence.");
    }
    const childTotal = subOrder.totalAmount ?? subOrder.total;
    const childShipping = subOrder.shippingAmount ?? subOrder.shipping;
    if (subOrder.subtotal !== undefined) {
      assertBreakdown(childTotal, subOrder.subtotal, childShipping, "Marketplace order financial authority rejected contradictory split totals.");
    } else if (!Array.isArray(subOrder.items) || subOrder.items.length === 0) {
      throw new Error("Marketplace order financial authority rejected missing split subtotal and item evidence.");
    }
    const acceptedChildTotal = requireMoney(childTotal, "Marketplace order financial authority rejected malformed split total.");
    const acceptedChildShipping = requireMoney(childShipping, "Marketplace order financial authority rejected malformed split shipping.");
    subOrderTotal += acceptedChildTotal;
    if (Array.isArray(subOrder.items)) {
      let itemTotal = 0;
      for (const item of subOrder.items as Array<Record<string, unknown>>) {
        itemTotal += requireMoney(item.total, "Marketplace order financial authority rejected malformed order-item amount.");
      }
      if (!sameMoney(acceptedChildTotal - acceptedChildShipping, itemTotal)) {
        throw new Error("Marketplace order financial authority rejected contradictory split item totals.");
      }
    }
  }

  if (!sameMoney(requireMoney(total, "Marketplace order financial authority rejected malformed order total."), subOrderTotal)) {
    throw new Error("Marketplace order financial authority rejected contradictory parent and split totals.");
  }
  return order;
}

export function requireCommerceHubCartPricingAuthority<T extends Record<string, unknown>>(pricing: T): T {
  assertBreakdown(pricing.total, pricing.subtotal, pricing.shippingTotal, "CommerceHub cart pricing authority rejected contradictory backend totals.");
  requireCommerceHubCurrency(pricing.currency, "CommerceHub cart pricing authority rejected non-EGP currency evidence.");
  if (!Array.isArray(pricing.storeSelections) || !Array.isArray(pricing.paymentMethods)) {
    throw new Error("CommerceHub cart pricing authority rejected malformed pricing evidence.");
  }
  let storeShippingTotal = 0;
  for (const selection of pricing.storeSelections as Array<Record<string, unknown>>) {
    storeShippingTotal += requireMoney(selection.shippingPrice, "CommerceHub cart pricing authority rejected malformed shipping evidence.");
  }
  if (!sameMoney(requireMoney(pricing.shippingTotal, "CommerceHub cart pricing authority rejected malformed shipping total."), storeShippingTotal)) {
    throw new Error("CommerceHub cart pricing authority rejected contradictory store shipping evidence.");
  }
  return pricing;
}

export function requireCommerceHubCartStateCurrencyAuthority<T extends Record<string, unknown>>(cart: T): T {
  if (!Array.isArray(cart.items)) {
    throw new Error("CommerceHub cart authority rejected malformed cart evidence.");
  }

  for (const item of cart.items as Array<Record<string, unknown>>) {
    requireCommerceHubCurrency(item.currency, "CommerceHub cart authority rejected non-EGP persisted-cart evidence.");
  }

  return cart;
}

export function requirePaymentAmountAuthority<T extends Record<string, unknown>>(payment: T, expectedAmount: number, expectedCurrency: string): T {
  requireCommerceHubCurrency(expectedCurrency, "Marketplace order financial authority rejected a non-EGP expected payment currency.");
  if (
    !isFiniteNonNegativeMoney(payment.amount) ||
    !sameMoney(payment.amount, expectedAmount) ||
    !isCommerceHubAuthorityCurrency(typeof payment.currency === "string" ? payment.currency : null)
  ) {
    throw new Error("Marketplace order financial authority rejected payment amount evidence for the current order.");
  }
  return payment;
}

export function requireCustomerRefundFinancialAuthority<T extends Record<string, unknown>>(refund: T): T {
  const amount = requireMoney(refund.amount, "Marketplace refund authority rejected malformed refund amount.");
  const unitPrice = requireMoney(refund.unitPriceSnapshot, "Marketplace refund authority rejected malformed refund unit price.");
  const quantity = refund.quantity;
  if (typeof quantity !== "number" || !Number.isInteger(quantity) || quantity <= 0 || !sameMoney(amount, unitPrice * quantity)) {
    throw new Error("Marketplace refund authority rejected contradictory refund financial evidence.");
  }
  requireCommerceHubCurrency(refund.currency, "Marketplace refund authority rejected non-EGP currency evidence.");
  return refund;
}
