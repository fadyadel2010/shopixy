import { customerOrderEndpoints } from "@/api/endpoints";
import { requireCustomerOrderDetailAuthority, requireCustomerOrdersListAuthority } from "@/features/customer/orderIdentityAuthority";
import { httpClient } from "@/api/http-client";
import type { CustomerOrderDetails, CustomerOrdersQuery, CustomerOrdersResponse } from "@/types";

export const customerOrdersService = {
  async getOrders(query: CustomerOrdersQuery = {}, signal?: AbortSignal) {
    const response = await httpClient.get<CustomerOrdersResponse>(customerOrderEndpoints.list, {
      params: query,
      signal
    });

    return requireCustomerOrdersListAuthority(response.data);
  },

  async getOrder(orderId: string, signal?: AbortSignal) {
    const response = await httpClient.get<CustomerOrderDetails>(customerOrderEndpoints.detail(orderId), {
      signal
    });

    return requireCustomerOrderDetailAuthority(response.data, orderId);
  }
};
