import type { CommerceHubCategory, CommerceHubProduct, CommerceHubProductDetail } from "@/types";
import { flattenCategoryTree } from "@/utils";

import type {
  CollectionStatus,
  HomepageSectionRecord,
  ProductsSectionData,
  ResolvedHomepageSection,
  ResolvedProductsSectionData
} from "./types";
import {
  normalizeHomepageCollectionLimit,
  normalizeHomepageGuidList,
  resolveHomepageCollectionSource,
  selectHomepageCollectionItems
} from "./homepageCollectionUtils";

export type ProductQueryState = {
  data?: CommerceHubProductDetail;
  isPending: boolean;
  isError: boolean;
  errorMessage?: string;
};

type ResolveHomepageSectionsArgs = {
  rawSections: HomepageSectionRecord[];
  categories: CommerceHubCategory[];
  categoriesStatus: CollectionStatus;
  categoriesErrorMessage?: string;
  productStatesById: Map<string, ProductQueryState>;
  automaticProducts: CommerceHubProduct[];
  automaticProductsStatus: CollectionStatus;
  automaticProductsErrorMessage?: string;
};

const mapProductDetailToSummary = (product: CommerceHubProductDetail): CommerceHubProduct => ({
  id: product.id,
  tenantId: product.tenantId,
  storeName: product.storeName,
  storeShippingFee: product.storeShippingFee,
  productId: product.productId,
  variantId: product.initialVariantId,
  productName: product.productName,
  image: product.image,
  price: product.price,
  currency: product.currency,
  categoryId: product.categoryId,
  categoryName: product.categoryName,
  categoryPath: product.categoryPath,
  inStock: product.inStock,
  availableStock: product.stockQuantity,
  isPublished: product.isPublished
});

const buildProductsState = (
  data: ProductsSectionData,
  productStatesById: Map<string, ProductQueryState>,
  automaticProducts: CommerceHubProduct[],
  automaticProductsStatus: CollectionStatus,
  automaticProductsErrorMessage: string | undefined,
  sectionId: string
): Pick<ResolvedProductsSectionData, "items" | "status" | "source" | "maxItems" | "errorMessage"> => {
  const normalizedProductIds = normalizeHomepageGuidList(data.productIds) ?? [];
  const source = resolveHomepageCollectionSource(data.source, normalizedProductIds);
  const maxItems = normalizeHomepageCollectionLimit(data.maxItems, 8, 24);

  if (source === "curated") {
    const items = normalizedProductIds
      .map((id) => productStatesById.get(id)?.data)
      .filter((product): product is CommerceHubProductDetail => Boolean(product))
      .map(mapProductDetailToSummary);

    const hasPending = normalizedProductIds.some((id) => productStatesById.get(id)?.isPending);
    const erroredStates = normalizedProductIds
      .map((id) => productStatesById.get(id))
      .filter((state): state is ProductQueryState => Boolean(state?.isError));

    const status: CollectionStatus =
      items.length > 0 ? "ready" : hasPending ? "loading" : erroredStates.length > 0 ? "error" : "empty";

    return {
      items,
      status,
      source,
      maxItems,
      errorMessage: erroredStates.map((state) => state.errorMessage).filter(Boolean)[0]
    };
  }

  const status: CollectionStatus =
    automaticProductsStatus === "loading"
      ? "loading"
      : automaticProductsStatus === "error"
        ? "error"
        : automaticProducts.length > 0
          ? "ready"
          : "empty";

  return {
    items:
      automaticProductsStatus === "ready"
        ? selectHomepageCollectionItems(automaticProducts, {
            limit: maxItems,
            source,
            seedKey: `${sectionId}:${data.title ?? "products"}:${data.layout ?? "grid"}`,
            getKey: (product) => product.id
          })
        : [],
    status,
    source,
    maxItems,
    errorMessage: automaticProductsStatus === "error" ? automaticProductsErrorMessage : undefined
  };
};

export const extractHomepageProductIds = (sections: HomepageSectionRecord[]) =>
  Array.from(
    new Set(
      sections.flatMap((section) =>
        section.type === "products" &&
        resolveHomepageCollectionSource(section.data.source, section.data.productIds) === "curated"
          ? (normalizeHomepageGuidList(section.data.productIds) ?? [])
          : []
      )
    )
  );

export const getHomepageAutomaticProductFetchSize = (sections: HomepageSectionRecord[]) =>
  sections.reduce((maxItems, section) => {
    if (section.type !== "products") {
      return maxItems;
    }

    const source = resolveHomepageCollectionSource(section.data.source, section.data.productIds);
    if (source === "curated") {
      return maxItems;
    }

    const limit = normalizeHomepageCollectionLimit(section.data.maxItems, 8, 24);
    const requiredFetchSize = source === "random" ? Math.max(limit * 3, 24) : limit;
    return Math.max(maxItems, requiredFetchSize);
  }, 0);

export const resolveHomepageSections = ({
  rawSections,
  categories,
  categoriesStatus,
  categoriesErrorMessage,
  productStatesById,
  automaticProducts,
  automaticProductsStatus,
  automaticProductsErrorMessage
}: ResolveHomepageSectionsArgs): ResolvedHomepageSection[] => {
  const flattenedCategories = flattenCategoryTree(categories);
  const categoryLookup = new Map(flattenedCategories.map((category) => [category.id, category]));

  return rawSections
    .map((section): ResolvedHomepageSection | null => {
      const sectionId = section.id;
      const sectionType = section.type;

      try {
        switch (sectionType) {
          case "hero":
            return {
              id: sectionId,
              type: "hero",
              data: section.data
            };
          case "banner":
            return {
              id: sectionId,
              type: "banner",
              data: section.data
            };
          case "products": {
            const productState = buildProductsState(
              section.data,
              productStatesById,
              automaticProducts,
              automaticProductsStatus,
              automaticProductsErrorMessage,
              sectionId
            );
            return {
              id: sectionId,
              type: "products",
              data: {
                ...section.data,
                ...productState
              }
            };
          }
          case "categories": {
            const normalizedCategoryIds = normalizeHomepageGuidList(section.data.categoryIds) ?? [];
            const source = resolveHomepageCollectionSource(section.data.source, normalizedCategoryIds);
            const maxItems = normalizeHomepageCollectionLimit(section.data.maxItems, 8, 18);
            const items =
              source === "curated"
                ? normalizedCategoryIds
                    .map((id) => categoryLookup.get(id))
                    .filter((category): category is CommerceHubCategory => Boolean(category))
                : selectHomepageCollectionItems(flattenedCategories, {
                    limit: maxItems,
                    source,
                    seedKey: `${sectionId}:${section.data.title ?? "categories"}:categories`,
                    getKey: (category) => category.id
                  });

            const status: CollectionStatus =
              items.length > 0
                ? "ready"
                : categoriesStatus === "loading"
                  ? "loading"
                  : categoriesStatus === "error"
                    ? "error"
                    : "empty";

            return {
              id: sectionId,
              type: "categories",
              data: {
                ...section.data,
                source,
                maxItems,
                items,
                status,
                errorMessage: categoriesStatus === "error" ? categoriesErrorMessage : undefined
              }
            };
          }
          default:
            console.warn("[Homepage] Skipping unknown homepage section type.", {
              sectionId,
              type: sectionType
            });
            return null;
        }
      } catch (error) {
        console.error("[Homepage] Skipping homepage section because it failed to resolve.", {
          sectionId,
          type: sectionType,
          error
        });

        return null;
      }
    })
    .filter((section): section is ResolvedHomepageSection => section !== null);
};
