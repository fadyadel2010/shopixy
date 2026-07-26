import { useCallback, useEffect, useLayoutEffect, useRef } from "react";

import { DISCOVERY_SCROLL_STORAGE_KEY } from "@/features/discovery/constants";

type ScrollRestoreEntry = {
  key: string;
  y: number;
};

type UseProductsScrollRestorationOptions = {
  restoreKey: string;
  itemCount: number;
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  fetchNextPage: () => Promise<unknown>;
};

const readStoredScrollEntry = (): ScrollRestoreEntry | null => {
  const raw = window.sessionStorage.getItem(DISCOVERY_SCROLL_STORAGE_KEY);

  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as ScrollRestoreEntry;
    return typeof parsed?.key === "string" && typeof parsed?.y === "number" ? parsed : null;
  } catch {
    return null;
  }
};

export const saveProductsScrollPosition = (restoreKey: string) => {
  window.sessionStorage.setItem(
    DISCOVERY_SCROLL_STORAGE_KEY,
    JSON.stringify({
      key: restoreKey,
      y: window.scrollY
    } satisfies ScrollRestoreEntry)
  );
};

export const useProductsScrollRestoration = ({
  restoreKey,
  itemCount,
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage
}: UseProductsScrollRestorationOptions) => {
  const pendingScrollYRef = useRef<number | null>(null);
  const restoredRef = useRef(false);
  const restoreKeyRef = useRef(restoreKey);

  useLayoutEffect(() => {
    restoredRef.current = false;
    restoreKeyRef.current = restoreKey;

    const storedEntry = readStoredScrollEntry();
    pendingScrollYRef.current = storedEntry?.key === restoreKey ? Math.max(0, storedEntry.y) : null;
  }, [restoreKey]);

  useEffect(() => {
    const pendingScrollY = pendingScrollYRef.current;

    if (pendingScrollY === null || restoredRef.current) {
      return;
    }

    const maxScrollableY = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);

    if (maxScrollableY >= pendingScrollY || !hasNextPage) {
      const nextScrollY = Math.min(pendingScrollY, maxScrollableY);

      window.requestAnimationFrame(() => {
        window.scrollTo({ top: nextScrollY, behavior: "auto" });
      });

      restoredRef.current = true;
      pendingScrollYRef.current = null;
      window.sessionStorage.removeItem(DISCOVERY_SCROLL_STORAGE_KEY);
      return;
    }

    if (!isFetchingNextPage) {
      void fetchNextPage();
    }
  }, [fetchNextPage, hasNextPage, isFetchingNextPage, itemCount]);

  return useCallback(() => {
    saveProductsScrollPosition(restoreKeyRef.current);
  }, []);
};
