import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MessageSquareQuote, PencilLine, ShieldCheck, Star, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { marketplaceReviewKeys, productRatingSummaryQueryOptions, productReviewContextQueryOptions, productReviewsQueryOptions } from "@/reviews";
import { marketplaceReviewsService } from "@/services";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import { selectCustomerProfile, selectIsCustomerAuthenticated, useCustomerAuthStore, useToastStore } from "@/store";
import type { MarketplaceProductReview, MarketplaceProductReviewSort, MarketplaceProductReviewStatus } from "@/types";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { Skeleton } from "@/ui/skeleton";

import { ProductRatingSummaryCard } from "./ProductRatingSummaryCard";
import { RatingStars } from "./RatingStars";

const REVIEW_PAGE_SIZE = 6;
const EDIT_COOLDOWN_MS = 15_000;
const PROJECTION_REFRESH_DELAY_MS = 1_500;

type ProductReviewsSectionProps = {
  productId: string;
  productName: string;
  showHeading?: boolean;
};

type FormNotice = {
  tone: "success" | "warning" | "error";
  message: string;
};

const sortOptions: { value: MarketplaceProductReviewSort; label: string }[] = [
  { value: "latest", label: "Latest" },
  { value: "highest", label: "Highest" },
  { value: "lowest", label: "Lowest" }
];

const formatReviewDate = (value?: string | null) => {
  if (!value) {
    return "Just now";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return "Just now";
  }

  return parsed.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric"
  });
};

const getStatusBadgeVariant = (status: MarketplaceProductReviewStatus) => {
  switch (status) {
    case "Approved":
      return { variant: "success" as const, className: "" };
    case "Rejected":
      return { variant: "muted" as const, className: "bg-rose-100 text-rose-700" };
    default:
      return { variant: "warning" as const, className: "" };
  }
};

const getReviewErrorMessage = (error: unknown) => {
  const apiError = normalizeApiError(error);

  if (apiError.status === 429 || apiError.code === "RATE_LIMITED") {
    return "Too many requests. Please try again later.";
  }

  if (apiError.code === "PURCHASE_REQUIRED") {
    return "Only customers with a delivered purchase can review this product.";
  }

  if (apiError.code === "REVIEW_ALREADY_EXISTS") {
    return "You already reviewed this product. Refreshing your latest review state now.";
  }

  return apiError.message || "We couldn't save your review right now.";
};

const scheduleProjectionRefresh = (queryClient: ReturnType<typeof useQueryClient>, productId: string) => {
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: marketplaceReviewKeys.summary(productId) });
    void queryClient.invalidateQueries({ queryKey: marketplaceReviewKeys.listRoot(productId) });
  };

  invalidate();
  window.setTimeout(invalidate, PROJECTION_REFRESH_DELAY_MS);
};

const ProductReviewCard = ({ review }: { review: MarketplaceProductReview }) => {
  const statusBadge = getStatusBadgeVariant(review.status);

  return (
    <Card className="border-white/75 bg-white/92">
      <CardContent className="space-y-4 p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-semibold text-foreground">{review.reviewerDisplayName || "Verified customer"}</p>
              {review.isVerifiedPurchase ? (
                <Badge variant="success" className="gap-1">
                  <ShieldCheck className="h-3.5 w-3.5" />
                  Verified Purchase
                </Badge>
              ) : null}
            </div>
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <RatingStars value={review.rating} size="sm" />
              <span>{formatReviewDate(review.updatedAtUtc ?? review.createdAtUtc)}</span>
            </div>
          </div>

          <Badge variant={statusBadge.variant} className={statusBadge.className}>
            {review.status}
          </Badge>
        </div>

        {review.title ? <h4 className="text-lg font-bold tracking-tight text-foreground">{review.title}</h4> : null}
        <p className="text-sm leading-7 text-muted-foreground">{review.comment}</p>
      </CardContent>
    </Card>
  );
};

export const ProductReviewsSection = ({ productId, productName, showHeading = true }: ProductReviewsSectionProps) => {
  const location = useLocation();
  const queryClient = useQueryClient();
  const customer = useCustomerAuthStore(selectCustomerProfile);
  const isAuthenticated = useCustomerAuthStore(selectIsCustomerAuthenticated);
  const showToast = useToastStore((state) => state.showToast);
  const customerScope = customer?.id ?? "guest";

  const [page, setPage] = useState(1);
  const [sort, setSort] = useState<MarketplaceProductReviewSort>("latest");
  const [verifiedOnly, setVerifiedOnly] = useState(false);
  const [withCommentsOnly, setWithCommentsOnly] = useState(false);
  const [rating, setRating] = useState(0);
  const [title, setTitle] = useState("");
  const [comment, setComment] = useState("");
  const [formNotice, setFormNotice] = useState<FormNotice | null>(null);
  const [cooldownEndsAt, setCooldownEndsAt] = useState<number | null>(null);
  const [cooldownRemaining, setCooldownRemaining] = useState(0);

  const summaryQuery = useQuery(productRatingSummaryQueryOptions(productId));
  const reviewsQuery = useQuery(
    productReviewsQueryOptions(productId, {
      page,
      pageSize: REVIEW_PAGE_SIZE,
      sort,
      verifiedOnly,
      withCommentsOnly
    })
  );
  const contextQuery = useQuery(productReviewContextQueryOptions(productId, customerScope));

  useEffect(() => {
    setPage(1);
  }, [sort, verifiedOnly, withCommentsOnly]);

  useEffect(() => {
    const totalPages = reviewsQuery.data?.totalPages ?? 0;
    if (page > 1 && totalPages === 0) {
      setPage(1);
      return;
    }

    if (page > totalPages && totalPages > 0) {
      setPage(totalPages);
    }
  }, [page, reviewsQuery.data?.totalPages]);

  const existingReviewIdentity = contextQuery.data?.existingReview
    ? `${contextQuery.data.existingReview.id}:${contextQuery.data.existingReview.updatedAtUtc ?? contextQuery.data.existingReview.createdAtUtc}`
    : "new";

  useEffect(() => {
    const existingReview = contextQuery.data?.existingReview;

    setRating(existingReview?.rating ?? 0);
    setTitle(existingReview?.title ?? "");
    setComment(existingReview?.comment ?? "");
  }, [existingReviewIdentity, contextQuery.data?.existingReview]);

  useEffect(() => {
    if (!cooldownEndsAt) {
      setCooldownRemaining(0);
      return;
    }

    const tick = () => {
      const remaining = Math.max(0, Math.ceil((cooldownEndsAt - Date.now()) / 1000));
      setCooldownRemaining(remaining);

      if (remaining <= 0) {
        setCooldownEndsAt(null);
      }
    };

    tick();
    const intervalId = window.setInterval(tick, 1000);

    return () => {
      window.clearInterval(intervalId);
    };
  }, [cooldownEndsAt]);

  const submitMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        rating,
        title: title.trim() || null,
        comment: comment.trim()
      };

      if (contextQuery.data?.existingReview) {
        return marketplaceReviewsService.updateReview(contextQuery.data.existingReview.id, payload);
      }

      return marketplaceReviewsService.createReview({
        productId,
        ...payload
      });
    },
    onSuccess: async (savedReview) => {
      const savedMessage =
        savedReview.status === "Approved"
          ? "Your review was saved. Marketplace ratings will refresh in a moment."
          : "Your review was saved and will appear after approval.";

      setFormNotice({
        tone: savedReview.status === "Approved" ? "success" : "warning",
        message: savedMessage
      });
      setCooldownEndsAt(Date.now() + EDIT_COOLDOWN_MS);

      showToast({
        title: contextQuery.data?.existingReview ? "Review updated" : "Review submitted",
        description: savedMessage,
        tone: "success"
      });

      await queryClient.invalidateQueries({ queryKey: marketplaceReviewKeys.context(productId, customerScope) });
      scheduleProjectionRefresh(queryClient, productId);
    },
    onError: async (error) => {
      const message = getReviewErrorMessage(error);
      setFormNotice({ tone: "error", message });

      showToast({
        title: "Review not saved",
        description: message,
        tone: "error"
      });

      if (normalizeApiError(error).code === "REVIEW_ALREADY_EXISTS") {
        await queryClient.invalidateQueries({ queryKey: marketplaceReviewKeys.context(productId, customerScope) });
      }
    }
  });

  const deleteMutation = useMutation({
    mutationFn: async () => {
      const reviewId = contextQuery.data?.existingReview?.id;
      if (!reviewId) {
        throw new Error("Review not found.");
      }

      await marketplaceReviewsService.deleteReview(reviewId);
    },
    onSuccess: async () => {
      setFormNotice({
        tone: "success",
        message: "Your review was deleted. If you want to post another one, you can submit a fresh review."
      });
      setCooldownEndsAt(null);

      showToast({
        title: "Review deleted",
        description: "Your review has been removed from the marketplace workflow.",
        tone: "success"
      });

      await queryClient.invalidateQueries({ queryKey: marketplaceReviewKeys.context(productId, customerScope) });
      scheduleProjectionRefresh(queryClient, productId);
    },
    onError: (error) => {
      const message = getReviewErrorMessage(error);
      setFormNotice({ tone: "error", message });
      showToast({
        title: "Delete failed",
        description: message,
        tone: "error"
      });
    }
  });

  const existingReview = contextQuery.data?.existingReview ?? null;
  const isSubmitting = submitMutation.isPending || deleteMutation.isPending;
  const submitDisabled = isSubmitting || cooldownRemaining > 0 || rating <= 0 || !comment.trim();
  const redirectTo = `${location.pathname}${location.search}`;
  const totalPages = reviewsQuery.data?.totalPages ?? 0;
  const reviewCount = reviewsQuery.data?.totalCount ?? 0;
  const reviewStatusBadge = existingReview ? getStatusBadgeVariant(existingReview.status) : null;

  return (
    <section className="space-y-6">
      {showHeading ? (
        <SectionHeading
          eyebrow="Trust signals"
          title="Ratings & reviews"
          description="Average rating and breakdown come directly from the marketplace review summary projection. Public reviews stay approved-only, paginated, and cache-backed for fast product pages."
        />
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,0.92fr)_minmax(0,1.08fr)]">
        <div className="space-y-6">
          {summaryQuery.isError && !summaryQuery.data ? (
            <InlineErrorState
              title="Rating summary unavailable"
              message="We couldn't load the rating summary right now."
              onRetry={() => void summaryQuery.refetch()}
            />
          ) : (
            <ProductRatingSummaryCard summary={summaryQuery.data} isLoading={summaryQuery.isLoading} />
          )}

          <Card className="border-white/80 bg-white/94">
            <CardContent className="space-y-5 p-6">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.24em] text-muted-foreground">Write a review</p>
                  <h3 className="mt-2 text-2xl font-black tracking-tight text-foreground">
                    {existingReview ? "Update your review" : `Review ${productName}`}
                  </h3>
                  <p className="mt-2 text-sm leading-7 text-muted-foreground">
                    Share what the product was actually like after delivery. Review submissions respect verified purchase rules and moderation automatically.
                  </p>
                </div>

                {existingReview && reviewStatusBadge ? (
                  <Badge variant={reviewStatusBadge.variant} className={reviewStatusBadge.className}>
                    {existingReview.status}
                  </Badge>
                ) : null}
              </div>

              {contextQuery.isLoading && !contextQuery.data ? (
                <div className="space-y-4">
                  <Skeleton className="h-5 w-44" />
                  <Skeleton className="h-12 w-full" />
                  <Skeleton className="h-12 w-full" />
                  <Skeleton className="h-32 w-full" />
                </div>
              ) : contextQuery.isError ? (
                <InlineErrorState
                  title="Review status unavailable"
                  message="We couldn't confirm your review eligibility right now."
                  onRetry={() => void contextQuery.refetch()}
                />
              ) : !isAuthenticated || !contextQuery.data?.isAuthenticated ? (
                <div className="space-y-4 rounded-[1.5rem] border border-dashed border-primary/25 bg-primary/5 p-5">
                  <p className="text-sm leading-7 text-muted-foreground">
                    Sign in with your Shopixy customer account to check purchase eligibility and write a review.
                  </p>
                  <Button asChild type="button">
                    <Link to="/login" state={{ redirectTo }}>
                      Sign in to review
                    </Link>
                  </Button>
                </div>
              ) : !contextQuery.data.hasVerifiedPurchase && !existingReview ? (
                <div className="space-y-4 rounded-[1.5rem] border border-amber-200 bg-amber-50/85 p-5">
                  <Badge variant="warning">Verified purchase required</Badge>
                  <p className="text-sm leading-7 text-amber-900">
                    {contextQuery.data.eligibilityMessage || "Only customers with a delivered purchase can review this product."}
                  </p>
                </div>
              ) : (
                <div className="space-y-5">
                  {formNotice ? (
                    <div
                      className={[
                        "rounded-[1.5rem] border px-4 py-4 text-sm leading-7",
                        formNotice.tone === "success"
                          ? "border-emerald-200 bg-emerald-50 text-emerald-900"
                          : formNotice.tone === "warning"
                            ? "border-amber-200 bg-amber-50 text-amber-900"
                            : "border-rose-200 bg-rose-50 text-rose-900"
                      ].join(" ")}
                    >
                      {formNotice.message}
                    </div>
                  ) : contextQuery.data?.eligibilityMessage ? (
                    <div className="rounded-[1.5rem] border border-slate-200 bg-slate-50/85 px-4 py-4 text-sm leading-7 text-slate-700">
                      {contextQuery.data.eligibilityMessage}
                    </div>
                  ) : null}

                  <div className="space-y-3">
                    <p className="text-sm font-semibold text-foreground">Your rating</p>
                    <div className="flex flex-wrap gap-2">
                      {Array.from({ length: 5 }).map((_, index) => {
                        const starValue = index + 1;
                        const isActive = rating >= starValue;

                        return (
                          <button
                            key={starValue}
                            type="button"
                            className={[
                              "inline-flex h-12 w-12 items-center justify-center rounded-2xl border transition",
                              isActive
                                ? "border-amber-300 bg-amber-50 text-amber-500 shadow-soft"
                                : "border-slate-200 bg-white text-slate-300 hover:border-amber-200 hover:text-amber-400"
                            ].join(" ")}
                            onClick={() => setRating(starValue)}
                            disabled={isSubmitting}
                            aria-label={`Rate ${starValue} out of 5`}
                          >
                            <Star className="h-5 w-5 fill-current" />
                          </button>
                        );
                      })}
                    </div>
                  </div>

                  <label className="block space-y-2">
                    <span className="text-sm font-semibold text-foreground">Title</span>
                    <Input
                      value={title}
                      onChange={(event) => setTitle(event.target.value)}
                      placeholder="Sum up your experience in a few words"
                      maxLength={200}
                      disabled={isSubmitting}
                    />
                  </label>

                  <label className="block space-y-2">
                    <span className="text-sm font-semibold text-foreground">Comment</span>
                    <textarea
                      value={comment}
                      onChange={(event) => setComment(event.target.value)}
                      rows={5}
                      maxLength={4000}
                      disabled={isSubmitting}
                      placeholder="What was the fit, quality, delivery experience, or overall impression after using it?"
                      className="flex min-h-[8rem] w-full rounded-[1.5rem] border border-white/75 bg-white/88 px-4 py-3 text-sm leading-7 shadow-[inset_0_1px_0_rgba(255,255,255,0.65),0_16px_32px_-28px_rgba(30,41,59,0.42)] transition-all placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                    />
                  </label>

                  <div className="flex flex-wrap items-center gap-3">
                    <Button type="button" disabled={submitDisabled} onClick={() => submitMutation.mutate()}>
                      {submitMutation.isPending
                        ? existingReview
                          ? "Saving review..."
                          : "Submitting review..."
                        : cooldownRemaining > 0
                          ? `Wait ${cooldownRemaining}s`
                          : existingReview
                            ? "Save changes"
                            : "Submit review"}
                    </Button>

                    {existingReview ? (
                      <Button
                        type="button"
                        variant="outline"
                        className="gap-2"
                        disabled={isSubmitting}
                        onClick={() => deleteMutation.mutate()}
                      >
                        <Trash2 className="h-4 w-4" />
                        Delete review
                      </Button>
                    ) : null}

                    {existingReview ? (
                      <div className="inline-flex items-center gap-2 rounded-full bg-secondary/70 px-3 py-2 text-xs font-semibold text-muted-foreground">
                        <PencilLine className="h-3.5 w-3.5 text-primary" />
                        Editing stays customer-owned and re-enters moderation automatically.
                      </div>
                    ) : null}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="space-y-5">
          <Card className="border-white/80 bg-white/94">
            <CardContent className="space-y-5 p-6">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.24em] text-muted-foreground">Approved reviews</p>
                  <h3 className="mt-2 text-2xl font-black tracking-tight text-foreground">Real feedback from Shopixy customers</h3>
                  <p className="mt-2 text-sm leading-7 text-muted-foreground">
                    Pending and rejected reviews stay hidden here. Edited approved reviews disappear temporarily until moderation finishes again.
                  </p>
                </div>

                <div className="inline-flex items-center gap-2 rounded-full bg-secondary/70 px-4 py-2 text-sm font-semibold text-muted-foreground">
                  <MessageSquareQuote className="h-4 w-4 text-primary" />
                  {reviewCount} public review{reviewCount === 1 ? "" : "s"}
                </div>
              </div>

              <div className="space-y-4 rounded-[1.75rem] border border-white/70 bg-secondary/55 p-4">
                <div className="flex flex-wrap gap-2">
                  {sortOptions.map((option) => (
                    <Button
                      key={option.value}
                      type="button"
                      variant={sort === option.value ? "default" : "outline"}
                      size="sm"
                      onClick={() => setSort(option.value)}
                    >
                      {option.label}
                    </Button>
                  ))}
                </div>

                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    variant={verifiedOnly ? "secondary" : "outline"}
                    size="sm"
                    onClick={() => setVerifiedOnly((current) => !current)}
                  >
                    Verified only
                  </Button>
                  <Button
                    type="button"
                    variant={withCommentsOnly ? "secondary" : "outline"}
                    size="sm"
                    onClick={() => setWithCommentsOnly((current) => !current)}
                  >
                    With comments only
                  </Button>
                </div>
              </div>

              {reviewsQuery.isLoading && !reviewsQuery.data ? (
                <div className="space-y-4">
                  {Array.from({ length: 3 }).map((_, index) => (
                    <Card key={index} className="border-white/75 bg-white/92">
                      <CardContent className="space-y-4 p-5">
                        <Skeleton className="h-5 w-40" />
                        <Skeleton className="h-4 w-28" />
                        <Skeleton className="h-4 w-3/4" />
                        <Skeleton className="h-16 w-full" />
                      </CardContent>
                    </Card>
                  ))}
                </div>
              ) : reviewsQuery.isError && !reviewsQuery.data ? (
                <InlineErrorState
                  title="Reviews unavailable"
                  message="We couldn't load approved reviews right now."
                  onRetry={() => void reviewsQuery.refetch()}
                />
              ) : (reviewsQuery.data?.items.length ?? 0) === 0 ? (
                <EmptyState
                  title="Be the first to review this product"
                  description="Once an approved review lands, it will show up here with the marketplace rating summary."
                />
              ) : (
                <div className="space-y-4">
                  {reviewsQuery.data?.items.map((review) => (
                    <ProductReviewCard key={review.id} review={review} />
                  ))}
                </div>
              )}

              {reviewsQuery.isError && reviewsQuery.data ? (
                <div className="rounded-[1.5rem] border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900">
                  Showing the last cached review page while a fresh fetch is retried.
                </div>
              ) : null}

              {totalPages > 1 ? (
                <div className="flex flex-col gap-4 rounded-[1.75rem] border border-slate-200 bg-slate-50/80 px-4 py-4 sm:flex-row sm:items-center sm:justify-between">
                  <p className="text-sm text-slate-600">
                    Page {reviewsQuery.data?.page ?? page} of {totalPages} with {reviewCount} matching review{reviewCount === 1 ? "" : "s"}.
                  </p>

                  <div className="flex items-center gap-3">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={page <= 1}
                      onClick={() => setPage((current) => Math.max(1, current - 1))}
                    >
                      Previous
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      disabled={page >= totalPages}
                      onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              ) : null}
            </CardContent>
          </Card>
        </div>
      </div>
    </section>
  );
};
