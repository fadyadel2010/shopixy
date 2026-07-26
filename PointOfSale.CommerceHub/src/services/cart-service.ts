import { commerceHubEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import {
  requireCommerceHubCartPricingAuthority,
  requireCommerceHubCartStateCurrencyAuthority
} from "@/features/orderFinancialAuthority";
import type {
  CommerceHubCartPricingRequest,
  CommerceHubCartPricingResponse,
  CommerceHubCartState,
  CommerceHubCartSyncRequest
} from "@/types";

export const cartService = {
  async getCart() {
    const response = await httpClient.get<CommerceHubCartState>(commerceHubEndpoints.cart);
    return requireCommerceHubCartStateCurrencyAuthority(response.data);
  },

  async saveCart(payload: CommerceHubCartSyncRequest) {
    const response = await httpClient.post<CommerceHubCartState>(commerceHubEndpoints.cart, payload);
    return requireCommerceHubCartStateCurrencyAuthority(response.data);
  },

  async clearCart() {
    const response = await httpClient.delete<CommerceHubCartState>(commerceHubEndpoints.cart);
    return requireCommerceHubCartStateCurrencyAuthority(response.data);
  },

  async priceCart(payload: CommerceHubCartPricingRequest) {
    const response = await httpClient.post<CommerceHubCartPricingResponse>(commerceHubEndpoints.cartPricing, payload);
    return requireCommerceHubCartPricingAuthority(response.data);
  }
};
