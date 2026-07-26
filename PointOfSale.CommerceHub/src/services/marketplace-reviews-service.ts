import { marketplaceReviewEndpoints } from "@/api/endpoints";
import { httpClient } from "@/api/http-client";
import type {
  MarketplaceCreateProductReviewRequest,
  MarketplaceCustomerProductReviewContext,
  MarketplaceProductRatingSummary,
  MarketplaceProductReview,
  MarketplaceProductReviewsPage,
  MarketplaceProductReviewsQuery,
  MarketplaceUpdateProductReviewRequest
} from "@/types";

export const marketplaceReviewsService = {
  async getRatingSummary(productId: string, signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceProductRatingSummary>(
      marketplaceReviewEndpoints.summary(productId),
      { signal }
    );

    return response.data;
  },

  async getReviews(productId: string, query: MarketplaceProductReviewsQuery = {}, signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceProductReviewsPage>(
      marketplaceReviewEndpoints.list(productId),
      {
        params: query,
        signal
      }
    );

    return response.data;
  },

  async getReviewContext(productId: string, signal?: AbortSignal) {
    const response = await httpClient.get<MarketplaceCustomerProductReviewContext>(
      marketplaceReviewEndpoints.context(productId),
      { signal }
    );

    return response.data;
  },

  async createReview(payload: MarketplaceCreateProductReviewRequest, signal?: AbortSignal) {
    const response = await httpClient.post<MarketplaceProductReview>(
      marketplaceReviewEndpoints.create,
      payload,
      { signal }
    );

    return response.data;
  },

  async updateReview(reviewId: string, payload: MarketplaceUpdateProductReviewRequest, signal?: AbortSignal) {
    const response = await httpClient.put<MarketplaceProductReview>(
      marketplaceReviewEndpoints.update(reviewId),
      payload,
      { signal }
    );

    return response.data;
  },

  async deleteReview(reviewId: string, signal?: AbortSignal) {
    await httpClient.delete(marketplaceReviewEndpoints.delete(reviewId), { signal });
  }
};
