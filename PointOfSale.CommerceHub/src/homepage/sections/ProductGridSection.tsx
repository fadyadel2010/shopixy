import type { ResolvedProductsSectionData } from "@/homepage/types";
import { ProductCard } from "@/commerce/components/ProductCard";
import { ProductGrid } from "@/commerce/components/ProductGrid";
import { ProductGridSkeleton } from "@/commerce/components/ProductGridSkeleton";
import { analyticsService } from "@/services";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionContainer } from "@/shared/SectionContainer";

type ProductGridSectionProps = {
  data: ResolvedProductsSectionData;
};

export const ProductGridSection = ({ data }: ProductGridSectionProps) => {
  const isCuratedSource = data.source === "curated";

  const productCards = data.items.map((product) => (
    <ProductCard
      key={product.id}
      product={product}
      onClick={(clickedProduct) =>
        analyticsService.trackProductClicked(clickedProduct.productId, clickedProduct.variantId, "homepage-products")
      }
    />
  ));

  return (
    <SectionContainer
      title={data.title}
      description={
        data.subtitle ??
        (isCuratedSource
          ? "Curated Shopixy picks selected directly from stored homepage section data."
          : "Live marketplace products flow into this homepage shelf automatically.")
      }
    >
      {data.status === "loading" ? <ProductGridSkeleton count={data.maxItems ?? 8} /> : null}

      {data.status === "error" && data.errorMessage ? (
        <InlineErrorState title="Products unavailable" message={data.errorMessage} />
      ) : null}

      {data.status === "empty" ? (
        <EmptyState
          title={isCuratedSource ? "No curated products yet" : "No products available yet"}
          description={
            isCuratedSource
              ? "Choose one or more product IDs in the Homepage Manager and Shopixy will render them here."
              : data.source === "random"
                ? "Random homepage shelves pull from your published marketplace catalog automatically."
                : "Published marketplace products will appear here automatically."
          }
        />
      ) : null}

      {data.status === "ready" && data.items.length > 0 ? (
        data.layout === "carousel" ? (
          <div className="grid auto-cols-[minmax(16rem,1fr)] grid-flow-col gap-4 overflow-x-auto pb-2">
            {productCards}
          </div>
        ) : (
          <ProductGrid>{productCards}</ProductGrid>
        )
      ) : null}
    </SectionContainer>
  );
};
