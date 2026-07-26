export type MarketplaceProductReviewStatus = "Pending" | "Approved" | "Rejected";

export type MarketplaceProductReviewSort = "latest" | "highest" | "lowest";

export type MarketplaceProductReview = {
  id: string;
  productId: string;
  userId: string;
  reviewerDisplayName?: string | null;
  rating: number;
  title?: string | null;
  comment: string;
  isVerifiedPurchase: boolean;
  status: MarketplaceProductReviewStatus;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

export type MarketplaceProductReviewsQuery = {
  page?: number;
  pageSize?: number;
  sort?: MarketplaceProductReviewSort;
  verifiedOnly?: boolean;
  withCommentsOnly?: boolean;
};

export type MarketplaceProductReviewsPage = {
  items: MarketplaceProductReview[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type MarketplaceProductRatingSummary = {
  productId: string;
  averageRating: number;
  totalReviews: number;
  ratingBreakdown: Record<number, number>;
  lastUpdatedAtUtc: string;
};

export type MarketplaceCreateProductReviewRequest = {
  productId: string;
  rating: number;
  title?: string | null;
  comment: string;
};

export type MarketplaceUpdateProductReviewRequest = {
  rating: number;
  title?: string | null;
  comment: string;
};

export type MarketplaceCustomerProductReviewContext = {
  productId: string;
  isAuthenticated: boolean;
  hasVerifiedPurchase: boolean;
  hasExistingReview: boolean;
  canSubmitReview: boolean;
  canEditReview: boolean;
  eligibilityMessage?: string | null;
  existingReview?: MarketplaceProductReview | null;
};
