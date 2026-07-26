import { Skeleton } from "@/ui/skeleton";
import { cn } from "@/utils/cn";

type LoadingSkeletonProps = {
  className?: string;
  showMedia?: boolean;
  lines?: number;
};

export const LoadingSkeleton = ({ className, showMedia = false, lines = 3 }: LoadingSkeletonProps) => {
  return (
    <div className={cn("rounded-[1.75rem] border border-white/70 bg-white/75 p-5 shadow-soft", className)}>
      {showMedia ? <Skeleton className="mb-4 h-40 w-full rounded-[1.5rem]" /> : null}
      <div className="space-y-3">
        <Skeleton className="h-4 w-24 rounded-full" />
        {Array.from({ length: lines }).map((_, index) => (
          <Skeleton key={index} className={cn("h-4", index === lines - 1 ? "w-2/3" : "w-full")} />
        ))}
      </div>
    </div>
  );
};
