import { Skeleton } from "@/ui/skeleton";

export const ProductCardSkeleton = () => {
  return (
    <div className="rounded-[2rem] border border-white/70 bg-card/90 p-5 shadow-card">
      <Skeleton className="aspect-[4/3] w-full rounded-[1.5rem]" />
      <div className="mt-4 flex h-[11rem] flex-col gap-4">
        <div className="space-y-3">
          <Skeleton className="h-4 w-4/5" />
          <Skeleton className="h-4 w-3/5" />
          <Skeleton className="h-4 w-2/5" />
        </div>
        <div className="mt-auto flex items-end justify-between gap-3">
          <Skeleton className="h-6 w-24" />
          <Skeleton className="h-6 w-20 rounded-full" />
        </div>
      </div>
    </div>
  );
};
