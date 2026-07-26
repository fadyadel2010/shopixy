import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { CommerceHubApiError, normalizeApiError } from "@/api/api-error";
import { cartService } from "@/services";
import { selectCheckoutPayload, useCartStore } from "@/store";
import { buildCommerceHubCartPricingRequest, type CheckoutCartPayload, type CommerceHubCartPricingSelection } from "@/types";

export const commerceHubCartKeys = {
  all: ["commercehub", "cart"] as const,
  pricing: (request: ReturnType<typeof buildCommerceHubCartPricingRequest>) =>
    [...commerceHubCartKeys.all, "pricing", request] as const
};

export const useCommerceHubCartPricing = (
  cart: CheckoutCartPayload,
  selections: CommerceHubCartPricingSelection[] = [],
  promoCode?: string | null
) => {
  const hasHydrated = useCartStore((state) => state.hasHydrated);
  const hasBootstrapped = useCartStore((state) => state.hasBootstrapped);
  const isBootstrapping = useCartStore((state) => state.isBootstrapping);
  const request = useMemo(
    () =>
      buildCommerceHubCartPricingRequest(
        cart,
        selections.map((selection) => ({
          storeId: selection.storeId,
          shippingOptionId: selection.shippingOptionId
        })),
        promoCode
      ),
    [cart, selections, promoCode]
  );

  const shippingSelections = useMemo(
    () =>
      selections.map((selection) => ({
        storeId: selection.storeId,
        shippingOptionId: selection.shippingOptionId
      })),
    [selections]
  );

  const isRequestValid =
    hasHydrated &&
    hasBootstrapped &&
    !isBootstrapping &&
    request.stores.length > 0 &&
    request.stores.every((store) => store.items.length > 0);

  return useQuery({
    queryKey: commerceHubCartKeys.pricing(request),
    queryFn: async () => {
      try {
        return await cartService.priceCart(request);
      } catch (error) {
        const apiError = normalizeApiError(error);

        if (apiError.code !== "CART_MISMATCH") {
          throw apiError;
        }

        await useCartStore.getState().saveCart({ persistIfAvailableOnly: false });
        const authoritativePayload = selectCheckoutPayload(useCartStore.getState());
        const authoritativeRequest = buildCommerceHubCartPricingRequest(authoritativePayload, shippingSelections, promoCode);

        if (authoritativeRequest.stores.length === 0 || authoritativeRequest.stores.some((store) => store.items.length === 0)) {
          throw new CommerceHubApiError({
            message: "Your cart changed while we were validating it. Review the updated cart and try again.",
            status: 409,
            code: "CART_CHANGED"
          });
        }

        return await cartService.priceCart(authoritativeRequest);
      }
    },
    enabled: isRequestValid,
    retry: (failureCount, error) => {
      const apiError = normalizeApiError(error);
      return failureCount < 1 && (apiError.isNetworkError || apiError.status === null || apiError.status >= 500);
    }
  });
};
