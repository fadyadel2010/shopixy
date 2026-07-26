import { customerRefundEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import {
  requireCustomerCreatedRefundForCurrentOrderItem,
  requireCustomerRefundVisibilityAuthority
} from "@/features/customer/paymentVisibilityAuthority";
import { requireCustomerRefundFinancialAuthority } from "@/features/orderFinancialAuthority";
import type {
  CustomerCreateRefundRequest,
  CustomerRefund,
  CustomerRefundUploadResponse
} from "@/types";

export const customerRefundsService = {
  async getRefunds(signal?: AbortSignal) {
    const response = await httpClient.get<CustomerRefund[]>(customerRefundEndpoints.list, {
      signal
    });

    return response.data.map((refund) => requireCustomerRefundVisibilityAuthority(requireCustomerRefundFinancialAuthority(refund)));
  },

  async createRefund(request: CustomerCreateRefundRequest, idempotencyKey: string) {
    const response = await httpClient.post<CustomerRefund>(customerRefundEndpoints.list, request, {
      headers: {
        "Idempotency-Key": idempotencyKey
      }
    });
    return requireCustomerCreatedRefundForCurrentOrderItem(
      requireCustomerRefundFinancialAuthority(response.data),
      request.orderItemId
    );
  },

  async uploadEvidence(
    file: File,
    sha256: string,
    onProgress?: (percentage: number) => void
  ) {
    const formData = new FormData();
    formData.append("file", file);
    formData.append("sha256", sha256);

    const response = await httpClient.post<CustomerRefundUploadResponse>(
      customerRefundEndpoints.upload,
      formData,
      {
        headers: {
          "Content-Type": "multipart/form-data"
        },
        onUploadProgress: (event) => {
          if (!onProgress || !event.total) {
            return;
          }

          onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)));
        }
      }
    );

    return response.data;
  }
};
