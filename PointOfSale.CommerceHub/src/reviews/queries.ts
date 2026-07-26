import { queryOptions } from "@tanstack/react-query";

import { marketplaceReviewsService } from "@/services/marketplace-reviews-service";
import type { MarketplaceProductReviewsQuery } from "@/types";

export const marketplaceReviewKeys = {
  all: ["marketplace", "product-reviews"] as const,
  summary: (productId: string) => [...marketplaceReviewKeys.all, "summary", productId] as const,
  list: (productId: string, query: MarketplaceProductReviewsQuery) =>
    [...marketplaceReviewKeys.all, "list", productId, query] as const,
  listRoot: (productId: string) => [...marketplaceReviewKeys.all, "list", productId] as const,
  context: (productId: string, customerScope: string) =>
    [...marketplaceReviewKeys.all, "context", productId, customerScope] as const
};

export const productRatingSummaryQueryOptions = (productId: string) =>
  queryOptions({
    queryKey: marketplaceReviewKeys.summary(productId),
    queryFn: () => marketplaceReviewsService.getRatingSummary(productId),
    enabled: Boolean(productId)
  });

export const productReviewsQueryOptions = (productId: string, query: MarketplaceProductReviewsQuery) =>
  queryOptions({
    queryKey: marketplaceReviewKeys.list(productId, query),
    queryFn: () => marketplaceReviewsService.getReviews(productId, query),
    enabled: Boolean(productId),
    placeholderData: (previous) => previous
  });

export const productReviewContextQueryOptions = (productId: string, customerScope: string) =>
  queryOptions({
    queryKey: marketplaceReviewKeys.context(productId, customerScope),
    queryFn: () => marketplaceReviewsService.getReviewContext(productId),
    enabled: Boolean(productId)
  });
