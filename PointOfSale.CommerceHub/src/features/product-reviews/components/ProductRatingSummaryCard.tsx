import { MessageSquareText, Sparkles } from "lucide-react";

import type { MarketplaceProductRatingSummary } from "@/types";
import { Card, CardContent } from "@/ui/card";
import { Skeleton } from "@/ui/skeleton";

import { RatingStars } from "./RatingStars";

type ProductRatingSummaryCardProps = {
  summary?: MarketplaceProductRatingSummary;
  isLoading: boolean;
};

const ratings = [5, 4, 3, 2, 1] as const;

const getBreakdownCount = (summary: MarketplaceProductRatingSummary | undefined, rating: number) =>
  summary?.ratingBreakdown?.[rating] ?? 0;

export const ProductRatingSummaryCard = ({ summary, isLoading }: ProductRatingSummaryCardProps) => {
  if (isLoading && !summary) {
    return (
      <Card className="border-white/80 bg-white/92">
        <CardContent className="space-y-5 p-6">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-14 w-28" />
          <Skeleton className="h-5 w-32" />
          <div className="space-y-3">
            {ratings.map((rating) => (
              <div key={rating} className="flex items-center gap-3">
                <Skeleton className="h-4 w-10" />
                <Skeleton className="h-3 flex-1" />
                <Skeleton className="h-4 w-8" />
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    );
  }

  const averageRating = summary?.averageRating ?? 0;
  const totalReviews = summary?.totalReviews ?? 0;

  return (
    <Card className="overflow-hidden border-white/80 bg-white/94">
      <div className="brand-gradient h-2 w-full" />
      <CardContent className="space-y-6 p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-muted-foreground">Rating summary</p>
            <h3 className="mt-2 text-2xl font-black tracking-tight text-foreground">What shoppers are saying</h3>
          </div>
          <div className="rounded-[1.25rem] bg-secondary/70 p-3 text-primary">
            <Sparkles className="h-5 w-5" />
          </div>
        </div>

        <div className="grid gap-5 md:grid-cols-[auto_minmax(0,1fr)] md:items-center">
          <div className="rounded-[1.75rem] border border-white/70 bg-secondary/65 px-5 py-5 text-center">
            <p className="text-4xl font-black tracking-tight text-foreground">
              {totalReviews === 0 ? "0.0" : averageRating.toFixed(1)}
            </p>
            <div className="mt-3 flex justify-center">
              <RatingStars value={averageRating} size="md" />
            </div>
            <div className="mt-3 inline-flex items-center gap-2 rounded-full bg-white px-3 py-2 text-xs font-semibold text-muted-foreground shadow-soft">
              <MessageSquareText className="h-4 w-4 text-primary" />
              {totalReviews} review{totalReviews === 1 ? "" : "s"}
            </div>
          </div>

          <div className="space-y-3">
            {ratings.map((rating) => {
              const count = getBreakdownCount(summary, rating);
              const percent = totalReviews > 0 ? Math.round((count / totalReviews) * 100) : 0;

              return (
                <div key={rating} className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 text-sm">
                  <span className="font-semibold text-foreground">{rating}★</span>
                  <div className="h-3 overflow-hidden rounded-full bg-secondary">
                    <div
                      className="h-full rounded-full bg-[linear-gradient(90deg,#f59e0b_0%,#fb7185_100%)]"
                      style={{ width: `${percent}%` }}
                    />
                  </div>
                  <span className="min-w-[3.5rem] text-right font-medium text-muted-foreground">{count} ({percent}%)</span>
                </div>
              );
            })}
          </div>
        </div>

        {totalReviews === 0 ? (
          <div className="rounded-[1.5rem] border border-dashed border-primary/20 bg-primary/5 px-4 py-4 text-sm text-muted-foreground">
            No ratings yet. Once approved reviews start landing, this summary will update automatically from the marketplace read model.
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
};
