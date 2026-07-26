import { commerceHubEndpoints } from "@/api/endpoints";
import { requireRecoverableOrderAuthority } from "@/features/customer/orderIdentityAuthority";
import { httpClient } from "@/api/http-client";
import type { CommerceHubRecoverableOrder } from "@/types";

export const orderRecoveryService = {
  async getOrder(orderId: string, orderAccessToken?: string | null, signal?: AbortSignal) {
    const response = await httpClient.get<CommerceHubRecoverableOrder>(commerceHubEndpoints.orderDetail(orderId), {
      params: orderAccessToken ? { token: orderAccessToken } : undefined,
      signal
    });

    return requireRecoverableOrderAuthority(response.data, orderId);
  }
};
