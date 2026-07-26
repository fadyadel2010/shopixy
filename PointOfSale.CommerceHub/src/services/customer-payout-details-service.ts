import { customerPayoutDetailsEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import type { CustomerPayoutDetails, CustomerUpsertPayoutDetailsRequest } from "@/types";

export const customerPayoutDetailsService = {
  async getPayoutDetails(signal?: AbortSignal) {
    const response = await httpClient.get<CustomerPayoutDetails[]>(customerPayoutDetailsEndpoints.list, {
      signal
    });

    return response.data;
  },

  async createPayoutDetails(request: CustomerUpsertPayoutDetailsRequest) {
    const response = await httpClient.post<CustomerPayoutDetails>(customerPayoutDetailsEndpoints.list, request);
    return response.data;
  },

  async updatePayoutDetails(id: string, request: CustomerUpsertPayoutDetailsRequest) {
    const response = await httpClient.put<CustomerPayoutDetails>(customerPayoutDetailsEndpoints.detail(id), request);
    return response.data;
  }
};
