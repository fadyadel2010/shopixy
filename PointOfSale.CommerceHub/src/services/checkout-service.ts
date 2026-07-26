import { commerceHubEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import { requireCheckoutOrderCreationAuthority, requirePaymentInitiationAuthority } from "@/features/checkout/orderCreationAuthority";
import type {
  CommerceHubCheckoutRequest,
  CommerceHubCheckoutResponse,
  CommerceHubPaymentInitiateRequest,
  CommerceHubPaymentInitiateResponse
} from "@/types";

export const checkoutService = {
  async placeOrder(payload: CommerceHubCheckoutRequest, idempotencyKey: string) {
    const response = await httpClient.post<CommerceHubCheckoutResponse>(commerceHubEndpoints.checkout, payload, {
      headers: {
        "Idempotency-Key": idempotencyKey
      }
    });
    return requireCheckoutOrderCreationAuthority(response.data);
  },

  async initiatePayment(payload: CommerceHubPaymentInitiateRequest, expectedAmount: number, expectedCurrency: string) {
    const response = await httpClient.post<CommerceHubPaymentInitiateResponse>(commerceHubEndpoints.paymentInitiate, payload);
    return requirePaymentInitiationAuthority(response.data, payload.orderId, payload.provider, expectedAmount, expectedCurrency);
  }
};
