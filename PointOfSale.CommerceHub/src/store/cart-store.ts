import { create } from "zustand";
import { persist } from "zustand/middleware";

import { CommerceHubApiError, normalizeApiError } from "@/api/api-error";
import { cartService } from "@/services";
import {
  buildCartStoresMapFromServerCart,
  buildCheckoutCartPayload,
  buildCommerceHubCartSyncRequest,
  clampCartItemQuantity,
  getCartSummary,
  getCartValidation,
  getCommerceHubCartSyncFingerprint,
  mergeCartStores,
  type CartItem,
  type CartItemInput,
  type CartStoresMap,
  type CommerceHubCartState
} from "@/types/cart";
import { sanitizePriceSnapshot } from "@/utils/price";
import { COMMERCEHUB_AUTHORITY_CURRENCY } from "@/utils/commerceHubCurrencyAuthority";

type SaveCartOptions = {
  persistIfAvailableOnly?: boolean;
};

const CART_CONFLICT_MESSAGE = "Your cart changed in another tab and was refreshed. Review it before continuing.";
const CART_LOCKED_MESSAGE = "Your cart is locked during checkout. Refresh the cart after checkout finishes.";
const GUEST_CART_SCOPE_KEY = "guest";
const CUSTOMER_CART_SCOPE_PREFIX = "customer:";

type CartItemSyncInput = {
  listingId?: string;
  maxQuantity?: number | null;
  isInStock?: boolean;
  inventoryNotice?: string | null;
  shippingFee?: number;
  currency?: string;
};

type CartState = {
  stores: CartStoresMap;
  version: number;
  promoCode: string | null;
  scopeKey: string;
  adoptGuestCartOnBootstrap: boolean;
  isLocked: boolean;
  lockedAtUtc: string | null;
  isCheckoutLocked: boolean;
  hasHydrated: boolean;
  isBootstrapping: boolean;
  hasBootstrapped: boolean;
  isServerBacked: boolean;
  isSyncing: boolean;
  syncError: string | null;
  lastSyncedAt: number | null;
  lastSyncedFingerprint: string | null;
  setHasHydrated: (hasHydrated: boolean) => void;
  setCheckoutLock: (isLocked: boolean) => void;
  setSyncError: (syncError: string | null) => void;
  setPromoCode: (promoCode: string | null) => void;
  clearPromoCode: () => void;
  synchronizeAuthScope: (customerId: string | null) => void;
  applyBackendCart: (cartState: CommerceHubCartState) => void;
  bootstrapCart: () => Promise<CommerceHubCartState | null>;
  saveCart: (options?: SaveCartOptions) => Promise<CommerceHubCartState | null>;
  addItem: (item: CartItemInput) => void;
  removeItem: (storeId: string, variantId: string) => void;
  updateQuantity: (storeId: string, variantId: string, quantity: number) => void;
  syncItem: (storeId: string, variantId: string, snapshot: CartItemSyncInput) => void;
  clearStore: (storeId: string) => void;
  clearCart: () => Promise<CommerceHubCartState | null>;
};

type CartPersistedState = Pick<CartState, "stores" | "version" | "promoCode" | "scopeKey" | "adoptGuestCartOnBootstrap">;

const parseUpdatedAtUtc = (updatedAtUtc: string | undefined) => {
  const parsed = updatedAtUtc ? Date.parse(updatedAtUtc) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : Date.now();
};

const normalizeCustomerScopeSegment = (customerId: string | null | undefined) => customerId?.trim().toLowerCase() ?? "";

const buildCartScopeKey = (customerId: string | null | undefined) => {
  const normalizedCustomerId = normalizeCustomerScopeSegment(customerId);
  return normalizedCustomerId ? `${CUSTOMER_CART_SCOPE_PREFIX}${normalizedCustomerId}` : GUEST_CART_SCOPE_KEY;
};

const isCustomerScopeKey = (scopeKey: string | null | undefined) => scopeKey?.startsWith(CUSTOMER_CART_SCOPE_PREFIX) ?? false;

const createCartConflictError = (details?: unknown) =>
  new CommerceHubApiError({
    message: CART_CONFLICT_MESSAGE,
    status: 409,
    code: "CART_CONFLICT",
    details
  });

const isCartLocked = (state: Pick<CartState, "isLocked" | "isCheckoutLocked">) => state.isLocked || state.isCheckoutLocked;

const upsertCartItem = (items: CartItem[], incoming: CartItemInput) => {
  const existingItem = items.find((item) => item.variantId === incoming.variantId);
  const nextItem: CartItem = {
    storeId: incoming.storeId,
    listingId: incoming.listingId,
    productId: incoming.productId,
    variantId: incoming.variantId,
    // S40: Preserve existing SKU when incoming.sku is undefined
    sku: incoming.sku ?? existingItem?.sku ?? "",
    name: incoming.name,
    priceSnapshot: sanitizePriceSnapshot(incoming.priceSnapshot),
    quantity: clampCartItemQuantity(incoming.quantity, incoming.maxQuantity),
    imageUrl: incoming.imageUrl,
    variantLabel: incoming.variantLabel,
    variantAttributes: incoming.variantAttributes,
    maxQuantity: incoming.maxQuantity,
    isInStock: incoming.isInStock,
    inventoryNotice: incoming.inventoryNotice ?? null
  };

  if (!existingItem) {
    return [...items, nextItem];
  }

  return items.map((item) =>
    item.variantId === incoming.variantId
      ? {
          ...item,
          ...nextItem,
          quantity: clampCartItemQuantity(item.quantity + incoming.quantity, incoming.maxQuantity ?? item.maxQuantity)
        }
      : item
  );
};

const applyBackendCartState = (cartState: CommerceHubCartState): Partial<CartState> => {
  const stores = buildCartStoresMapFromServerCart(cartState);

    return {
      stores,
      isLocked: cartState.isLocked,
      lockedAtUtc: cartState.lockedAtUtc ?? null,
      version: Number.isFinite(cartState.version) ? Math.max(0, Math.floor(cartState.version)) : 0,
      adoptGuestCartOnBootstrap: false,
      isServerBacked: cartState.isServerBacked,
      isSyncing: false,
      syncError: null,
    lastSyncedAt: parseUpdatedAtUtc(cartState.updatedAtUtc),
    lastSyncedFingerprint: getCommerceHubCartSyncFingerprint(stores)
  };
};

export const useCartStore = create<CartState>()(
  persist(
    (set, get) => {
      const refreshAfterConflict = async () => {
        const latestCart = await cartService.getCart();
        set({
          ...applyBackendCartState(latestCart),
          promoCode: latestCart.items.length === 0 ? null : get().promoCode,
          syncError: CART_CONFLICT_MESSAGE
        });

        return latestCart;
      };

      const refreshAfterLockedCart = async () => {
        const latestCart = await cartService.getCart();
        set({
          ...applyBackendCartState(latestCart),
          promoCode: latestCart.items.length === 0 ? null : get().promoCode,
          syncError: CART_LOCKED_MESSAGE
        });

        return latestCart;
      };

      return {
        stores: {},
        version: 0,
        promoCode: null,
        scopeKey: GUEST_CART_SCOPE_KEY,
        adoptGuestCartOnBootstrap: false,
        isLocked: false,
        lockedAtUtc: null,
        isCheckoutLocked: false,
        hasHydrated: false,
        isBootstrapping: false,
        hasBootstrapped: false,
        isServerBacked: false,
        isSyncing: false,
        syncError: null,
        lastSyncedAt: null,
        lastSyncedFingerprint: null,

        setHasHydrated: (hasHydrated) => set({ hasHydrated }),
        setCheckoutLock: (isLocked) => set({ isCheckoutLocked: isLocked }),
        setSyncError: (syncError) => set({ syncError }),
        setPromoCode: (promoCode) => set({ promoCode: promoCode?.trim() ? promoCode.trim().toUpperCase() : null }),
        clearPromoCode: () => set({ promoCode: null }),
        synchronizeAuthScope: (customerId) =>
          set((state) => {
            const nextScopeKey = buildCartScopeKey(customerId);
            if (state.scopeKey === nextScopeKey) {
              return state;
            }

            const hasLocalItems = Object.keys(state.stores).length > 0;
            const currentScopeIsCustomer = isCustomerScopeKey(state.scopeKey);
            const nextScopeIsCustomer = isCustomerScopeKey(nextScopeKey);

            if (!currentScopeIsCustomer && nextScopeIsCustomer) {
              return {
                // Keep the guest snapshot until bootstrap adopts it into the
                // authenticated server cart. Clearing it here loses the cart
                // before the merge path can persist customer ownership.
                stores: state.stores,
                promoCode: state.promoCode,
                scopeKey: nextScopeKey,
                adoptGuestCartOnBootstrap: hasLocalItems,
                version: 0,
                isLocked: false,
                lockedAtUtc: null,
                isCheckoutLocked: false,
                isBootstrapping: false,
                hasBootstrapped: false,
                isServerBacked: false,
                isSyncing: false,
                syncError: null,
                lastSyncedAt: null,
                lastSyncedFingerprint: null
              };
            }

            return {
              stores: {},
              version: 0,
              promoCode: null,
              scopeKey: nextScopeKey,
              adoptGuestCartOnBootstrap: false,
              isLocked: false,
              lockedAtUtc: null,
              isCheckoutLocked: false,
              isBootstrapping: false,
              hasBootstrapped: false,
              isServerBacked: false,
              isSyncing: false,
              syncError: null,
              lastSyncedAt: null,
              lastSyncedFingerprint: null
            };
          }),

        applyBackendCart: (cartState) => {
          set({
            ...applyBackendCartState(cartState),
            adoptGuestCartOnBootstrap: false,
            promoCode: cartState.items.length === 0 ? null : get().promoCode
          });
        },

        bootstrapCart: async () => {
          if (get().isBootstrapping) {
            return null;
          }

          set({
            isBootstrapping: true,
            syncError: null
          });

        try {
          const localStores = get().stores;
          const hasLocalItems = Object.keys(localStores).length > 0;
          const serverCart = await cartService.getCart();
          const shouldAdoptGuestCart = get().adoptGuestCartOnBootstrap;

          if (!serverCart.isServerBacked) {
            if (hasLocalItems) {
              const sanitizedLocalCart = await cartService.saveCart(buildCommerceHubCartSyncRequest(localStores, 0));
              set({
                ...applyBackendCartState(sanitizedLocalCart),
                adoptGuestCartOnBootstrap: false,
                promoCode: sanitizedLocalCart.items.length === 0 ? null : get().promoCode,
                isBootstrapping: false,
                hasBootstrapped: true
              });

              return sanitizedLocalCart;
            }

            set({
              isServerBacked: false,
              isLocked: false,
              lockedAtUtc: null,
              version: 0,
              promoCode: null,
              adoptGuestCartOnBootstrap: false,
              isBootstrapping: false,
              hasBootstrapped: true,
              syncError: null,
              lastSyncedAt: null,
              lastSyncedFingerprint: null
            });

            return serverCart;
          }

          if (!shouldAdoptGuestCart) {
            set({
              ...applyBackendCartState(serverCart),
              adoptGuestCartOnBootstrap: false,
              promoCode: null,
              isBootstrapping: false,
              hasBootstrapped: true
            });

            return serverCart;
          }

          if (serverCart.isLocked) {
            set({
              ...applyBackendCartState(serverCart),
              adoptGuestCartOnBootstrap: false,
              promoCode: null,
              isBootstrapping: false,
              hasBootstrapped: true
            });

            return serverCart;
          }

          const serverStores = buildCartStoresMapFromServerCart(serverCart);
          const hasServerItems = serverCart.items.length > 0;

          if (hasLocalItems && hasServerItems) {
            const mergedStores = mergeCartStores(localStores, serverStores);
            set({
              stores: mergedStores,
              adoptGuestCartOnBootstrap: true,
              isLocked: false,
              lockedAtUtc: null,
              isServerBacked: true,
              version: serverCart.version,
              syncError: null
            });

            const mergedCart = await get().saveCart({ persistIfAvailableOnly: false });
            set({
              isBootstrapping: false,
              hasBootstrapped: true
            });

            return mergedCart;
          }

          if (hasLocalItems && !hasServerItems) {
            set({
              adoptGuestCartOnBootstrap: true,
              isLocked: false,
              lockedAtUtc: null,
              isServerBacked: true,
              version: serverCart.version,
              syncError: null
            });

            const syncedLocalCart = await get().saveCart({ persistIfAvailableOnly: false });
            set({
              isBootstrapping: false,
              hasBootstrapped: true
            });

            return syncedLocalCart;
          }

          set({
            ...applyBackendCartState(serverCart),
            adoptGuestCartOnBootstrap: false,
            promoCode: null,
            isBootstrapping: false,
            hasBootstrapped: true
          });

          return serverCart;
        } catch (error) {
          const apiError = normalizeApiError(error);
          set((state) => ({
            isBootstrapping: false,
            hasBootstrapped: true,
            syncError: state.syncError ?? apiError.message
          }));

          return null;
        }
        },

        saveCart: async (options) => {
          const persistIfAvailableOnly = options?.persistIfAvailableOnly ?? true;

        if (persistIfAvailableOnly && !get().isServerBacked) {
          return null;
        }

        set({
          isSyncing: true,
          syncError: null
        });

        try {
          const response = await cartService.saveCart(buildCommerceHubCartSyncRequest(get().stores, get().version));
          set({
            ...applyBackendCartState(response),
            adoptGuestCartOnBootstrap: false,
            promoCode: response.items.length === 0 ? null : get().promoCode
          });
          return response;
        } catch (error) {
          const apiError = normalizeApiError(error);

          if (apiError.code === "CART_CONFLICT") {
            try {
              await refreshAfterConflict();
            } catch {
              set({
                isSyncing: false,
                syncError: CART_CONFLICT_MESSAGE
              });
            }

            throw createCartConflictError(apiError.details);
          }

          if (apiError.code === "CART_LOCKED") {
            try {
              await refreshAfterLockedCart();
            } catch {
              set({
                isSyncing: false,
                syncError: CART_LOCKED_MESSAGE
              });
            }

            throw apiError;
          }

          set({
            isSyncing: false,
            syncError: apiError.message
          });
          throw apiError;
        }
        },

        addItem: (incoming) =>
          set((state) => {
            if (isCartLocked(state)) {
              return state;
            }

            const existingStore = state.stores[incoming.storeId];

          return {
            stores: {
              ...state.stores,
              [incoming.storeId]: {
                storeName: incoming.storeName,
                shippingFee: incoming.shippingFee,
                currency: COMMERCEHUB_AUTHORITY_CURRENCY,
                items: upsertCartItem(existingStore?.items ?? [], incoming)
              }
            }
          };
          }),

        removeItem: (storeId, variantId) =>
          set((state) => {
            if (isCartLocked(state)) {
              return state;
            }

            const targetStore = state.stores[storeId];
            if (!targetStore) {
              return state;
            }

          const nextItems = targetStore.items.filter((item) => item.variantId !== variantId);
          if (nextItems.length === 0) {
            const { [storeId]: _removed, ...remainingStores } = state.stores;
            return { stores: remainingStores };
          }

          return {
            stores: {
              ...state.stores,
              [storeId]: {
                ...targetStore,
                items: nextItems
              }
            }
          };
          }),

        updateQuantity: (storeId, variantId, quantity) =>
          set((state) => {
            if (isCartLocked(state)) {
              return state;
            }

            const targetStore = state.stores[storeId];
            if (!targetStore) {
              return state;
            }

          const targetItem = targetStore.items.find((item) => item.variantId === variantId);
          if (!targetItem) {
            return state;
          }

          const nextQuantity = clampCartItemQuantity(quantity, targetItem.maxQuantity);
          if (nextQuantity === targetItem.quantity) {
            return state;
          }

          return {
            stores: {
              ...state.stores,
              [storeId]: {
                ...targetStore,
                items: targetStore.items.map((item) =>
                  item.variantId === variantId
                    ? {
                        ...item,
                        quantity: nextQuantity
                      }
                    : item
                )
              }
            }
          };
          }),

        syncItem: (storeId, variantId, snapshot) =>
          set((state) => {
            const targetStore = state.stores[storeId];
            if (!targetStore) {
              return state;
            }

          const targetItem = targetStore.items.find((item) => item.variantId === variantId);
          if (!targetItem) {
            return state;
          }

          const nextMaxQuantity = snapshot.maxQuantity ?? targetItem.maxQuantity;
          const nextIsInStock = snapshot.isInStock ?? targetItem.isInStock;
          const nextQuantity =
            typeof nextMaxQuantity === "number" && nextMaxQuantity > 0
              ? Math.min(targetItem.quantity, nextMaxQuantity)
              : targetItem.quantity;
          const shouldClearRecoveredNotice =
            nextIsInStock &&
            (typeof nextMaxQuantity !== "number" || nextMaxQuantity > targetItem.quantity) &&
            Boolean(targetItem.inventoryNotice);
          const nextNotice =
            snapshot.inventoryNotice !== undefined
              ? snapshot.inventoryNotice
              : !nextIsInStock || (typeof nextMaxQuantity === "number" && nextMaxQuantity < 1)
                ? "This item is currently out of stock."
                : typeof nextMaxQuantity === "number" && targetItem.quantity > nextMaxQuantity
                  ? `Only ${nextMaxQuantity} item${nextMaxQuantity === 1 ? "" : "s"} available. Quantity was updated.`
                  : shouldClearRecoveredNotice
                    ? null
                    : targetItem.inventoryNotice ?? null;

          const nextItem: CartItem = {
            ...targetItem,
            listingId: snapshot.listingId ?? targetItem.listingId,
            maxQuantity: nextMaxQuantity,
            isInStock: nextIsInStock,
            quantity: nextQuantity,
            inventoryNotice: nextNotice
          };

          const storeShippingFee = snapshot.shippingFee ?? targetStore.shippingFee;
          const storeCurrency = COMMERCEHUB_AUTHORITY_CURRENCY;
          const changed =
            nextItem.listingId !== targetItem.listingId ||
            nextItem.maxQuantity !== targetItem.maxQuantity ||
            nextItem.isInStock !== targetItem.isInStock ||
            nextItem.quantity !== targetItem.quantity ||
            nextItem.inventoryNotice !== targetItem.inventoryNotice ||
            storeShippingFee !== targetStore.shippingFee ||
            storeCurrency !== targetStore.currency;

          if (!changed) {
            return state;
          }

          return {
            stores: {
              ...state.stores,
              [storeId]: {
                ...targetStore,
                shippingFee: storeShippingFee,
                currency: storeCurrency,
                items: targetStore.items.map((item) => (item.variantId === variantId ? nextItem : item))
              }
            }
          };
          }),

        clearStore: (storeId) =>
          set((state) => {
            if (isCartLocked(state)) {
              return state;
            }

            const { [storeId]: _removed, ...remainingStores } = state.stores;
            return { stores: remainingStores };
          }),

        clearCart: async () => {
          const shouldClearRemotely = get().isServerBacked;

        if (!shouldClearRemotely) {
          set({
            stores: {},
            promoCode: null,
            adoptGuestCartOnBootstrap: false,
            isLocked: false,
            lockedAtUtc: null,
            version: 0,
            syncError: null,
            lastSyncedAt: null,
            lastSyncedFingerprint: null
          });

          return null;
        }

        set({
          isSyncing: true,
          syncError: null
        });

        try {
          const response = await cartService.clearCart();
          set({
            ...applyBackendCartState(response),
            adoptGuestCartOnBootstrap: false,
            promoCode: null
          });
          return response;
        } catch (error) {
          const apiError = normalizeApiError(error);

          if (apiError.code === "CART_CONFLICT") {
            try {
              await refreshAfterConflict();
            } catch {
              set({
                isSyncing: false,
                syncError: CART_CONFLICT_MESSAGE
              });
            }

            throw createCartConflictError(apiError.details);
          }

          if (apiError.code === "CART_LOCKED") {
            try {
              await refreshAfterLockedCart();
            } catch {
              set({
                isSyncing: false,
                syncError: CART_LOCKED_MESSAGE
              });
            }

            throw apiError;
          }

          set({
            isSyncing: false,
            syncError: apiError.message
          });
          throw apiError;
        }
        }
      };
    },
    {
      name: "commercehub-cart",
      partialize: (state): CartPersistedState => ({
        stores: state.stores,
        version: state.version,
        promoCode: state.promoCode,
        scopeKey: state.scopeKey,
        adoptGuestCartOnBootstrap: state.adoptGuestCartOnBootstrap
      }),
      onRehydrateStorage: () => (state, error) => {
        state?.setHasHydrated(true);

        if (error) {
          state?.setSyncError("Saved cart data could not be restored.");
        }
      }
    }
  )
);

export const selectCartStoreIds = (state: CartState) => Object.keys(state.stores);

export const selectCartItemCount = (state: CartState) => getCartSummary(state.stores).itemCount;

export const selectCartSummary = (state: CartState) => getCartSummary(state.stores);

export const selectCartValidation = (state: CartState) => getCartValidation(state.stores);

export const selectCheckoutPayload = (state: CartState) => buildCheckoutCartPayload(state.stores, state.version);

export const selectIsCartLocked = (state: CartState) => isCartLocked(state);

export const selectPromoCode = (state: CartState) => state.promoCode;
