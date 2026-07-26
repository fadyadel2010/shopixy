import { cn } from "@/utils/cn";

type SkeletonProps = {
  className?: string;
};

export const Skeleton = ({ className }: SkeletonProps) => {
  return <div className={cn("skeleton-wave rounded-[1.25rem] bg-muted/80", className)} aria-hidden="true" />;
};
