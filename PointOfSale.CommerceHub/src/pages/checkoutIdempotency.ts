import type { CheckoutCartPayload } from "@/types/cart";
import type { CheckoutCustomerFormData, CheckoutPaymentMethod } from "@/types/checkout";
import { sanitizePriceSnapshotForFingerprint } from "@/utils/price";

const buildFallbackUuid = () => `${Date.now()}-${Math.random().toString(16).slice(2)}`;

const sortCartItems = (items: CheckoutCartPayload["stores"][number]["items"]) =>
  [...items].sort((left, right) => {
    const byListing = (left.listingId ?? "").localeCompare(right.listingId ?? "");
    if (byListing !== 0) {
      return byListing;
    }

    const byProduct = left.productId.localeCompare(right.productId);
    if (byProduct !== 0) {
      return byProduct;
    }

    return left.variantId.localeCompare(right.variantId);
  });

const hashFingerprint = (value: string) => {
  let hash = 2166136261;

  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return `chk_${(hash >>> 0).toString(16)}`;
};

export const createCheckoutAttemptIdempotencyKey = () =>
  typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : buildFallbackUuid();

export const buildCheckoutAttemptFingerprint = (
  cart: CheckoutCartPayload,
  customer: CheckoutCustomerFormData,
  paymentMethod: CheckoutPaymentMethod,
  promoCode?: string | null
) =>
  hashFingerprint(
    JSON.stringify({
      promoCode: promoCode?.trim().toUpperCase() ?? null,
      paymentMethod,
      customer: {
        fullName: customer.fullName.trim(),
        phoneNumber: customer.phoneNumber.trim(),
        addressLine: customer.addressLine.trim(),
        city: customer.city.trim(),
        area: customer.area.trim(),
        notes: customer.notes.trim()
      },
      summary: {
        cartVersion: cart.version,
        currency: cart.summary.currency,
        subtotal: Number(cart.summary.subtotal.toFixed(2)),
        shipping: Number(cart.summary.shipping.toFixed(2)),
        total: Number(cart.summary.total.toFixed(2)),
        itemCount: cart.summary.itemCount,
        storeCount: cart.summary.storeCount
      },
      stores: [...cart.stores]
        .sort((a, b) => a.storeId.localeCompare(b.storeId))
        .map((store) => ({
          storeId: store.storeId,
          currency: store.currency,
          subtotal: Number(store.subtotal.toFixed(2)),
          shipping: Number(store.shipping.toFixed(2)),
          total: Number(store.total.toFixed(2)),
          itemCount: store.itemCount,
          items: sortCartItems(store.items).map((item) => ({
            listingId: item.listingId ?? null,
            productId: item.productId,
            variantId: item.variantId,
            quantity: item.quantity,
            priceSnapshot: sanitizePriceSnapshotForFingerprint(item.priceSnapshot)
          }))
        }))
    })
  );
