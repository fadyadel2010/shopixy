import { Star } from "lucide-react";

import { cn } from "@/utils";

type RatingStarsProps = {
  value: number;
  size?: "sm" | "md" | "lg";
  className?: string;
};

const starSizeClasses = {
  sm: "h-4 w-4",
  md: "h-5 w-5",
  lg: "h-6 w-6"
} as const;

const clampRating = (value: number) => Math.min(5, Math.max(0, Number.isFinite(value) ? value : 0));

const StarRow = ({ size }: { size: keyof typeof starSizeClasses }) => (
  <div className="flex items-center gap-1">
    {Array.from({ length: 5 }).map((_, index) => (
      <Star key={index} className={cn(starSizeClasses[size], "fill-current")} />
    ))}
  </div>
);

export const RatingStars = ({ value, size = "md", className }: RatingStarsProps) => {
  const percent = (clampRating(value) / 5) * 100;

  return (
    <div className={cn("relative inline-flex", className)} aria-label={`${value.toFixed(1)} out of 5 stars`}>
      <div className="text-slate-200">
        <StarRow size={size} />
      </div>
      <div className="absolute inset-y-0 left-0 overflow-hidden text-amber-400" style={{ width: `${percent}%` }}>
        <StarRow size={size} />
      </div>
    </div>
  );
};
