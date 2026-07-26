import { Skeleton } from "@/ui/skeleton";

type SearchSkeletonProps = {
  rows?: number;
};

export const SearchSkeleton = ({ rows = 4 }: SearchSkeletonProps) => {
  return (
    <div className="space-y-3 p-3">
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="rounded-2xl border border-border/60 bg-white p-3">
          <Skeleton className="h-4 w-3/5" />
          <div className="mt-3 flex items-center justify-between gap-3">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-20" />
          </div>
        </div>
      ))}
    </div>
  );
};
