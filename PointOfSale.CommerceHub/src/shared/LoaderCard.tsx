import { Skeleton } from "@/ui/skeleton";

export const LoaderCard = () => {
  return (
    <div className="rounded-3xl border bg-card p-5 shadow-card">
      <Skeleton className="aspect-[4/3] w-full rounded-[1.5rem]" />
      <div className="mt-4 space-y-3">
        <Skeleton className="h-4 w-3/4" />
        <Skeleton className="h-4 w-1/2" />
        <Skeleton className="h-4 w-1/3" />
      </div>
    </div>
  );
};
