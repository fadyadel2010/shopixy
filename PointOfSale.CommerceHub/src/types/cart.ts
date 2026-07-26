import { sanitizePriceSnapshot } from '../utils/price';
import { COMMERCEHUB_AUTHORITY_CURRENCY } from '../utils/commerceHubCurrencyAuthority';

export type CartVariantAttribute = {
  name: string;
  value: string;
};

export type CartItem = {
  storeId: string;
  listingId?: string;
  productId: string;
  variantId: string;
  sku?: string | null;
  name: string;
  priceSnapshot: number;
  quantity: number;
  imageUrl?: string | null;
  variantLabel?: string;
  variantAttributes?: CartVariantAttribute[];
  maxQuantity?: number | null;
  isInStock: boolean;
  inventoryNotice?: string | null;
};

export type CartItemInput = Omit<CartItem, "quantity"> & {
  storeName: string;
  quantity: number;
  shippingFee: number;
  currency: string;
};

export type CartGroup = {
  storeName: string;
  shippingFee: number;
  currency: string;
  items: CartItem[];
};

export type CartStoresMap = Record<string, CartGroup>;

export type CartStoreSummary = {
  subtotal: number;
  shipping: number;
  total: number;
  itemCount: number;
};

export type CartSummary = {
  subtotal: number;
  shipping: number;
  total: number;
  itemCount: number;
  storeCount: number;
  currency: string;
};

export type CheckoutStorePayload = CartStoreSummary & {
  storeId: string;
  storeName: string;
  currency: string;
  items: CartItem[];
};

export type CheckoutCartPayload = {
  version: number;
  groupedCart: CartStoresMap;
  stores: CheckoutStorePayload[];
  summary: CartSummary;
};

export type CommerceHubCartSyncItem = {
  storeId?: string;
  listingId?: string;
  productId?: string;
  variantId?: string;
  quantity: number;
  priceSnapshot?: number;
  name?: string;
  imageUrl?: string | null;
};

export type CommerceHubCartSyncRequest = {
  version: number;
  items: CommerceHubCartSyncItem[];
};

export type CommerceHubCartNotice = {
  code: string;
  message: string;
  storeId?: string | null;
  productId?: string | null;
  variantId?: string | null;
  requestedQuantity?: number | null;
  appliedQuantity?: number | null;
};

export type CommerceHubCartStateItem = {
  storeId: string;
  storeName: string;
  currency: string;
  listingId: string;
  productId: string;
  variantId: string;
  quantity: number;
  priceSnapshot: number;
  name: string;
  imageUrl?: string | null;
  variantLabel?: string | null;
  variantAttributes: CartVariantAttribute[];
  maxQuantity?: number | null;
  isInStock: boolean;
  inventoryNotice?: string | null;
};

export type CommerceHubCartState = {
  cartId?: string | null;
  isServerBacked: boolean;
  isAuthenticatedOwner: boolean;
  isLocked: boolean;
  lockedAtUtc?: string | null;
  version: number;
  updatedAtUtc: string;
  items: CommerceHubCartStateItem[];
  notices: CommerceHubCartNotice[];
  warnings: string[];
};

export type CommerceHubCartPricingSelection = {
  storeId: string;
  shippingOptionId: string;
  shippingPrice: number;
  shippingName: string;
};

export type CommerceHubCartPricingPromo = {
  code: string;
  discountTotal: number;
  isApplied: boolean;
};

export type CommerceHubCartPaymentMethodAvailability = {
  code: "COD" | "PAYMOB" | "STRIPE";
  isAvailable: boolean;
  unavailableReasonCode?: string | null;
  unavailableReason?: string | null;
};

export type CommerceHubCartPricingRequest = {
  promoCode?: string;
  stores: {
    storeId: string;
    items: {
      listingId: string;
      productId: string;
      variantId: string;
      quantity: number;
    }[];
  }[];
  shippingSelections: {
    storeId: string;
    shippingOptionId: string;
  }[];
};

export type CommerceHubCartPricingResponse = {
  currency: string;
  subtotal: number;
  shippingTotal: number;
  total: number;
  promo?: CommerceHubCartPricingPromo | null;
  storeSelections: CommerceHubCartPricingSelection[];
  paymentMethods: CommerceHubCartPaymentMethodAvailability[];
};

export type CartValidationIssue = {
  storeId: string;
  variantId: string;
  productId: string;
  itemName: string;
  message: string;
  type: "invalid_quantity" | "out_of_stock" | "quantity_exceeds_stock";
};

export type CartValidation = {
  canCheckout: boolean;
  issues: CartValidationIssue[];
};

const DEFAULT_CURRENCY = COMMERCEHUB_AUTHORITY_CURRENCY;
const MAX_CART_ITEM_QUANTITY = 100;

const sanitizeQuantity = (quantity: number) => {
  if (!Number.isFinite(quantity)) {
    return 1;
  }

  return Math.max(1, Math.min(MAX_CART_ITEM_QUANTITY, Math.floor(quantity)));
};

const sanitizeShippingFee = (shippingFee: number | undefined) => {
  if (typeof shippingFee !== "number" || Number.isNaN(shippingFee)) {
    return 0;
  }

  return Math.max(0, shippingFee);
};

const resolveMaxQuantity = (maxQuantity?: number | null) => {
  if (typeof maxQuantity !== "number" || Number.isNaN(maxQuantity) || maxQuantity < 1) {
    return MAX_CART_ITEM_QUANTITY;
  }

  return Math.max(1, Math.min(MAX_CART_ITEM_QUANTITY, Math.floor(maxQuantity)));
};

const sanitizeVersion = (version: number | undefined) => {
  if (typeof version !== "number" || Number.isNaN(version)) {
    return 0;
  }

  return Math.max(0, Math.floor(version));
};

const sortStores = (stores: CartStoresMap) =>
  Object.entries(stores).sort(([leftId, leftStore], [rightId, rightStore]) => {
    const byName = leftStore.storeName.localeCompare(rightStore.storeName, undefined, { sensitivity: "base" });
    return byName !== 0 ? byName : leftId.localeCompare(rightId);
  });

const buildCartItemKey = (item: Pick<CartItem, "storeId" | "variantId">) => [item.storeId, item.variantId].join(":");

export const clampCartItemQuantity = (quantity: number, maxQuantity?: number | null) => {
  return Math.min(sanitizeQuantity(quantity), resolveMaxQuantity(maxQuantity));
};

export const getCartItemSubtotal = (item: CartItem) => sanitizePriceSnapshot(item.priceSnapshot) * item.quantity;

export const getCartItems = (stores: CartStoresMap) =>
  sortStores(stores).flatMap(([storeId, store]) =>
    store.items.map((item) => ({
      storeId,
      storeName: store.storeName,
      currency: DEFAULT_CURRENCY,
      shippingFee: store.shippingFee,
      item
    }))
  );

export const getCartValidation = (stores: CartStoresMap): CartValidation => {
  const issues: CartValidationIssue[] = [];

  getCartItems(stores).forEach(({ storeId, item }) => {
    if (!Number.isFinite(item.quantity) || item.quantity < 1 || item.quantity > MAX_CART_ITEM_QUANTITY) {
      issues.push({
        storeId,
        variantId: item.variantId,
        productId: item.productId,
        itemName: item.name,
        message: `Quantity must be between 1 and ${MAX_CART_ITEM_QUANTITY}.`,
        type: "invalid_quantity"
      });
      return;
    }

    if (!item.isInStock || (typeof item.maxQuantity === "number" && item.maxQuantity < 1)) {
      issues.push({
        storeId,
        variantId: item.variantId,
        productId: item.productId,
        itemName: item.name,
        message: "This item is currently out of stock.",
        type: "out_of_stock"
      });
      return;
    }

    if (typeof item.maxQuantity === "number" && item.quantity > item.maxQuantity) {
      issues.push({
        storeId,
        variantId: item.variantId,
        productId: item.productId,
        itemName: item.name,
        message: `Only ${item.maxQuantity} item${item.maxQuantity === 1 ? "" : "s"} available.`,
        type: "quantity_exceeds_stock"
      });
    }
  });

  return {
    canCheckout: issues.length === 0 && Object.keys(stores).length > 0,
    issues
  };
};

export const getCartStoreSummary = (store: CartGroup): CartStoreSummary => {
  const subtotal = store.items.reduce((total, item) => total + getCartItemSubtotal(item), 0);
  const itemCount = store.items.reduce((total, item) => total + item.quantity, 0);
  const shipping = sanitizeShippingFee(store.shippingFee);

  return {
    subtotal,
    shipping,
    total: subtotal + shipping,
    itemCount
  };
};

export const getCartSummary = (stores: CartStoresMap): CartSummary => {
  const entries = Object.values(stores);
  const summary = entries.reduce(
    (totals, store) => {
      const storeSummary = getCartStoreSummary(store);

      return {
        subtotal: totals.subtotal + storeSummary.subtotal,
        shipping: totals.shipping + storeSummary.shipping,
        total: totals.total + storeSummary.total,
        itemCount: totals.itemCount + storeSummary.itemCount
      };
    },
    {
      subtotal: 0,
      shipping: 0,
      total: 0,
      itemCount: 0
    }
  );

  return {
    ...summary,
    storeCount: entries.length,
    currency: DEFAULT_CURRENCY
  };
};

export const buildCheckoutCartPayload = (stores: CartStoresMap, version = 0): CheckoutCartPayload => {
  const entries = sortStores(stores);

  return {
    version: sanitizeVersion(version),
    groupedCart: stores,
    stores: entries.map(([storeId, store]) => ({
      storeId,
      storeName: store.storeName,
      currency: DEFAULT_CURRENCY,
      items: store.items,
      ...getCartStoreSummary(store)
    })),
    summary: getCartSummary(stores)
  };
};

const buildCommerceHubCartSyncItems = (stores: CartStoresMap): CommerceHubCartSyncItem[] =>
  sortStores(stores)
    .flatMap(([storeId, store]) =>
      store.items.map((item) => ({
        storeId,
        listingId: item.listingId,
        productId: item.productId,
        variantId: item.variantId,
        quantity: item.quantity,
        priceSnapshot: item.priceSnapshot,
        name: item.name,
        imageUrl: item.imageUrl ?? undefined
      }))
    )
    .sort((left, right) => {
      const byStore = (left.storeId ?? "").localeCompare(right.storeId ?? "");
      if (byStore !== 0) {
        return byStore;
      }

      const byListing = (left.listingId ?? "").localeCompare(right.listingId ?? "");
      if (byListing !== 0) {
        return byListing;
      }

      const byProduct = (left.productId ?? "").localeCompare(right.productId ?? "");
      if (byProduct !== 0) {
        return byProduct;
        }

        return (left.variantId ?? "").localeCompare(right.variantId ?? "");
    });

export const buildCommerceHubCartSyncRequest = (
  stores: CartStoresMap,
  version = 0
): CommerceHubCartSyncRequest => ({
  version: sanitizeVersion(version),
  items: buildCommerceHubCartSyncItems(stores)
});

export const getCommerceHubCartSyncFingerprint = (stores: CartStoresMap) =>
  JSON.stringify(buildCommerceHubCartSyncItems(stores));

export const buildCartStoresMapFromServerCart = (cartState: CommerceHubCartState): CartStoresMap =>
  cartState.items.reduce<CartStoresMap>((stores, item) => {
    const existingStore = stores[item.storeId];

    stores[item.storeId] = {
      storeName: item.storeName,
      shippingFee: existingStore?.shippingFee ?? 0,
      currency: DEFAULT_CURRENCY,
      items: [
        ...(existingStore?.items ?? []),
        {
          storeId: item.storeId,
          listingId: item.listingId,
          productId: item.productId,
          variantId: item.variantId,
          name: item.name,
          priceSnapshot: sanitizePriceSnapshot(item.priceSnapshot),
          quantity: clampCartItemQuantity(item.quantity, item.maxQuantity),
          imageUrl: item.imageUrl ?? null,
          variantLabel: item.variantLabel ?? undefined,
          variantAttributes: item.variantAttributes,
          maxQuantity: item.maxQuantity ?? null,
          isInStock: item.isInStock,
          inventoryNotice: item.inventoryNotice ?? null
        }
      ]
    };

    return stores;
  }, {});

export const mergeCartStores = (localStores: CartStoresMap, serverStores: CartStoresMap): CartStoresMap => {
  const mergedStores: CartStoresMap = {};
  const allStoreIds = Array.from(new Set([...Object.keys(serverStores), ...Object.keys(localStores)]));

  allStoreIds.forEach((storeId) => {
    const localStore = localStores[storeId];
    const serverStore = serverStores[storeId];

    if (!localStore && !serverStore) {
      return;
    }

    if (!localStore) {
      mergedStores[storeId] = serverStore!;
      return;
    }

    if (!serverStore) {
      mergedStores[storeId] = localStore;
      return;
    }

    const itemMap = new Map<string, CartItem>();

    serverStore.items.forEach((item) => {
      itemMap.set(buildCartItemKey(item), { ...item });
    });

    localStore.items.forEach((item) => {
      const key = buildCartItemKey(item);
      const existing = itemMap.get(key);

      if (!existing) {
        itemMap.set(key, { ...item });
        return;
      }

      const mergedMaxQuantity =
        typeof existing.maxQuantity === "number"
          ? existing.maxQuantity
          : typeof item.maxQuantity === "number"
            ? item.maxQuantity
            : null;

      itemMap.set(key, {
        ...item,
        ...existing,
        quantity: clampCartItemQuantity(existing.quantity + item.quantity, mergedMaxQuantity)
      });
    });

    mergedStores[storeId] = {
      storeName: serverStore.storeName || localStore.storeName,
      shippingFee: serverStore.shippingFee || localStore.shippingFee,
      currency: DEFAULT_CURRENCY,
      items: Array.from(itemMap.values())
    };
  });

  return mergedStores;
};

export const buildCommerceHubCartPricingRequest = (
  cart: CheckoutCartPayload,
  selections: Array<{ storeId: string; shippingOptionId: string }> = [],
  promoCode?: string | null
): CommerceHubCartPricingRequest => ({
  promoCode: promoCode?.trim() ? promoCode.trim().toUpperCase() : undefined,
  stores: cart.stores
    .map((store) => ({
      storeId: store.storeId,
      items: store.items
        .filter((item) => item.listingId)
        .map((item) => ({
          listingId: item.listingId!,
          productId: item.productId,
          variantId: item.variantId,
          quantity: item.quantity
        }))
        .sort((left, right) => {
          const byListing = left.listingId.localeCompare(right.listingId);
          if (byListing !== 0) {
            return byListing;
          }

          const byProduct = left.productId.localeCompare(right.productId);
          if (byProduct !== 0) {
            return byProduct;
          }

          return left.variantId.localeCompare(right.variantId);
        })
    }))
    .filter((store) => store.items.length > 0)
    .sort((left, right) => left.storeId.localeCompare(right.storeId)),
  shippingSelections: [...selections].sort((left, right) => left.storeId.localeCompare(right.storeId))
});
