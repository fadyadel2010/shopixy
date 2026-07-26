import { ReactNode } from "react";

import { cn } from "@/utils/cn";

type ProductGridProps = {
  children: ReactNode;
  className?: string;
};

export const ProductGrid = ({ children, className }: ProductGridProps) => {
  return <div className={cn("grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4", className)}>{children}</div>;
};
