import { useCallback, useEffect, useMemo, useRef } from "react";
import { shallow } from "zustand/shallow";

import {
  selectCustomerAuthHydrated,
  selectCustomerProfile,
  useCartStore,
  useCustomerAuthStore,
  useToastStore
} from "@/store";
import { type CommerceHubCartNotice, getCommerceHubCartSyncFingerprint } from "@/types";
import { useDebouncedCallback } from "@/utils/useDebouncedCallback";

const GUEST_CART_SCOPE_KEY = "guest";
const CUSTOMER_CART_SCOPE_PREFIX = "customer:";

const buildNoticeKey = (notice: CommerceHubCartNotice) =>
  [
    notice.code,
    notice.storeId ?? "",
    notice.productId ?? "",
    notice.variantId ?? "",
    notice.requestedQuantity ?? "",
    notice.appliedQuantity ?? "",
    notice.message
  ].join(":");

const getNoticeTitle = (notice: CommerceHubCartNotice) => {
  if (notice.code.includes("REMOVED")) {
    return "Cart item removed";
  }

  if (notice.code.includes("PRICE_CHANGED")) {
    return "Price updated";
  }

  if (notice.code.includes("ADJUSTED") || notice.code.includes("CLAMPED")) {
    return "Cart quantity updated";
  }

  return "Cart updated";
};

const buildCartScopeKey = (customerId: string | null | undefined) => {
  const normalizedCustomerId = customerId?.trim().toLowerCase() ?? "";
  return normalizedCustomerId ? `${CUSTOMER_CART_SCOPE_PREFIX}${normalizedCustomerId}` : GUEST_CART_SCOPE_KEY;
};

export const CommerceHubCartSync = () => {
  const {
    stores,
    scopeKey,
    hasHydrated,
    hasBootstrapped,
    isBootstrapping,
    isSyncing,
    syncError,
    lastSyncedFingerprint,
    synchronizeAuthScope,
    bootstrapCart,
    saveCart
  } = useCartStore(
    (state) => ({
      stores: state.stores,
      scopeKey: state.scopeKey,
      hasHydrated: state.hasHydrated,
      hasBootstrapped: state.hasBootstrapped,
      isBootstrapping: state.isBootstrapping,
      isSyncing: state.isSyncing,
      syncError: state.syncError,
      lastSyncedFingerprint: state.lastSyncedFingerprint,
      synchronizeAuthScope: state.synchronizeAuthScope,
      bootstrapCart: state.bootstrapCart,
      saveCart: state.saveCart
    }),
    shallow
  );
  const hasCustomerAuthHydrated = useCustomerAuthStore(selectCustomerAuthHydrated);
  const customer = useCustomerAuthStore(selectCustomerProfile);
  const showToast = useToastStore((state) => state.showToast);
  const seenNoticeKeysRef = useRef<Set<string>>(new Set());
  const lastErrorRef = useRef<string | null>(null);
  const fingerprint = useMemo(() => getCommerceHubCartSyncFingerprint(stores), [stores]);
  const expectedScopeKey = useMemo(() => buildCartScopeKey(customer?.id ?? null), [customer?.id]);

  const showCartNotices = useCallback(
    (notices: CommerceHubCartNotice[]) => {
      notices.forEach((notice) => {
        const key = buildNoticeKey(notice);
        if (seenNoticeKeysRef.current.has(key)) {
          return;
        }

        seenNoticeKeysRef.current.add(key);
        showToast({
          title: getNoticeTitle(notice),
          description: notice.message,
          tone: "error"
        });
      });
    },
    [showToast]
  );

  useEffect(() => {
    if (!hasHydrated || !hasCustomerAuthHydrated) {
      return;
    }

    synchronizeAuthScope(customer?.id ?? null);
  }, [customer?.id, hasCustomerAuthHydrated, hasHydrated, synchronizeAuthScope]);

  useEffect(() => {
    if (!hasHydrated || !hasCustomerAuthHydrated || hasBootstrapped || isBootstrapping) {
      return;
    }

    if (scopeKey !== expectedScopeKey) {
      return;
    }

    void (async () => {
      const response = await bootstrapCart();
      if (response?.notices.length) {
        showCartNotices(response.notices);
      }
    })();
  }, [
    bootstrapCart,
    expectedScopeKey,
    hasBootstrapped,
    hasCustomerAuthHydrated,
    hasHydrated,
    isBootstrapping,
    scopeKey,
    showCartNotices
  ]);

  const syncCart = useCallback(async () => {
    try {
      const response = await saveCart({ persistIfAvailableOnly: false });
      if (response?.notices.length) {
        showCartNotices(response.notices);
      }
    } catch {
      // Store state already captures the sync error for UI/toasts.
    }
  }, [saveCart, showCartNotices]);

  const debouncedSync = useDebouncedCallback(() => {
    void syncCart();
  }, 1000);

  useEffect(() => {
    if (!hasHydrated || !hasCustomerAuthHydrated || !hasBootstrapped || isBootstrapping || isSyncing) {
      return;
    }

    if (scopeKey !== expectedScopeKey) {
      return;
    }

    if (fingerprint === lastSyncedFingerprint) {
      return;
    }

    if (lastSyncedFingerprint === null && Object.keys(stores).length === 0) {
      return;
    }

    debouncedSync();
  }, [
    debouncedSync,
    fingerprint,
    hasBootstrapped,
    hasCustomerAuthHydrated,
    hasHydrated,
    isBootstrapping,
    isSyncing,
    expectedScopeKey,
    scopeKey,
    stores,
    lastSyncedFingerprint
  ]);

  useEffect(() => {
    if (!syncError) {
      lastErrorRef.current = null;
      return;
    }

    if (lastErrorRef.current === syncError) {
      return;
    }

    lastErrorRef.current = syncError;
    showToast({
      title: "Cart sync issue",
      description: syncError,
      tone: "error"
    });
  }, [showToast, syncError]);

  return null;
};
