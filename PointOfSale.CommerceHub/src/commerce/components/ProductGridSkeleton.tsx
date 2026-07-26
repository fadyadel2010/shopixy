import { ProductCardSkeleton } from "@/commerce/components/ProductCardSkeleton";
import { ProductGrid } from "@/commerce/components/ProductGrid";

type ProductGridSkeletonProps = {
  count?: number;
};

export const ProductGridSkeleton = ({ count = 8 }: ProductGridSkeletonProps) => {
  return (
    <ProductGrid>
      {Array.from({ length: count }).map((_, index) => (
        <ProductCardSkeleton key={index} />
      ))}
    </ProductGrid>
  );
};
