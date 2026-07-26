import { Link } from "react-router-dom";

import { CategoryCard } from "@/commerce/components/CategoryCard";
import type { ResolvedCategoriesSectionData } from "@/homepage/types";
import { analyticsService } from "@/services";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { LoadingSkeleton } from "@/shared/LoadingSkeleton";
import { SectionContainer } from "@/shared/SectionContainer";

type CategorySectionProps = {
  data: ResolvedCategoriesSectionData;
};

export const CategorySection = ({ data }: CategorySectionProps) => {
  const isCuratedSource = data.source === "curated";

  return (
    <SectionContainer
      title={data.title}
      description={
        data.subtitle ??
        (isCuratedSource
          ? "Shopixy category navigation is now driven by persisted homepage configuration."
          : "Live marketplace categories flow into this homepage section automatically.")
      }
    >
      {data.status === "loading" ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: data.maxItems ?? 8 }).map((_, index) => (
            <LoadingSkeleton key={index} className="min-h-[168px]" lines={2} />
          ))}
        </div>
      ) : null}

      {data.status === "error" && data.errorMessage ? (
        <InlineErrorState title="Categories unavailable" message={data.errorMessage} />
      ) : null}

      {data.status === "empty" ? (
        <EmptyState
          title={isCuratedSource ? "No curated categories yet" : "No categories available yet"}
          description={
            isCuratedSource
              ? "Add category IDs in the Homepage Manager to turn this section into a curated discovery grid."
              : data.source === "random"
                ? "Random homepage category grids pull from your marketplace taxonomy automatically."
                : "Marketplace categories will appear here automatically."
          }
        />
      ) : null}

      {data.status === "ready" && data.items.length > 0 ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {data.items.map((category) => (
            <Link
              key={category.id}
              to={`/products?category=${category.id}`}
              className="block"
              onClick={() => analyticsService.trackCategorySelected(category.id, category.name, "homepage-categories")}
            >
              <CategoryCard category={category} />
            </Link>
          ))}
        </div>
      ) : null}
    </SectionContainer>
  );
};
