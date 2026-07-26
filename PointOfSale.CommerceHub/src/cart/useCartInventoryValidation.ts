import { useQueries } from "@tanstack/react-query";
import { useEffect, useMemo } from "react";
import { shallow } from "zustand/shallow";

import { productDetailQueryOptions } from "@/products";
import { useCartStore } from "@/store";
import { getCartItems } from "@/types/cart";

export const useCartInventoryValidation = () => {
  const stores = useCartStore((state) => state.stores, shallow);
  const syncItem = useCartStore((state) => state.syncItem);

  const cartEntries = useMemo(() => getCartItems(stores), [stores]);
  const listingIds = useMemo(
    () =>
      Array.from(
        new Set(
          cartEntries
            .map(({ item }) => item.listingId)
            .filter((listingId): listingId is string => Boolean(listingId))
        )
      ),
    [cartEntries]
  );

  const productQueries = useQueries({
    queries: listingIds.map((listingId) => ({
      ...productDetailQueryOptions(listingId),
      staleTime: 30_000,
      refetchOnWindowFocus: true
    }))
  });

  const listingMap = useMemo(() => {
    const map = new Map<string, (typeof productQueries)[number]["data"]>();

    listingIds.forEach((listingId, index) => {
      const detail = productQueries[index]?.data;
      if (detail) {
        map.set(listingId, detail);
      }
    });

    return map;
  }, [listingIds, productQueries]);

  useEffect(() => {
    cartEntries.forEach(({ storeId, item }) => {
      if (!item.listingId) {
        return;
      }

      const detail = listingMap.get(item.listingId);
      if (!detail) {
        return;
      }

      const matchedVariant = detail.variants.find((variant) => variant.id === item.variantId);
      if (!matchedVariant) {
        syncItem(storeId, item.variantId, {
          shippingFee: detail.storeShippingFee,
          currency: detail.currency,
          maxQuantity: 0,
          isInStock: false,
          inventoryNotice: "This variant is no longer available."
        });
        return;
      }

      syncItem(storeId, item.variantId, {
        shippingFee: detail.storeShippingFee,
        currency: detail.currency,
        maxQuantity: matchedVariant.totalStock,
        isInStock: matchedVariant.inStock
      });
    });
  }, [cartEntries, listingMap, syncItem]);

  const isInventoryRefreshing =
    productQueries.length > 0 && productQueries.some((query) => query.isPending || query.isFetching);
  const hasInventoryErrors = productQueries.some((query) => query.isError);

  return {
    isInventoryRefreshing,
    hasInventoryErrors
  };
};
