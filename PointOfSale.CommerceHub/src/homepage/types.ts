import type { CommerceHubCategory, CommerceHubProduct } from "@/types";

import type { HomepageCollectionSource } from "./homepageCollectionUtils";

export type HomepageSectionType = "hero" | "products" | "banner" | "categories";
export type CollectionStatus = "loading" | "ready" | "empty" | "error";

export type HeroSectionData = {
  title: string;
  subtitle: string;
  imageUrl: string;
  ctaText: string;
  ctaLink: string;
};

export type ProductsSectionData = {
  title: string;
  subtitle?: string | null;
  source?: HomepageCollectionSource | null;
  maxItems?: number | string | null;
  productIds?: string[];
  layout: "grid" | "carousel";
};

export type BannerSectionData = {
  title: string;
  subtitle: string;
  imageUrl: string;
  ctaText: string;
  ctaLink: string;
};

export type CategoriesSectionData = {
  title: string;
  subtitle?: string | null;
  source?: HomepageCollectionSource | null;
  maxItems?: number | string | null;
  categoryIds?: string[];
};

type HomepageSectionRecordBase<TType extends HomepageSectionType, TData> = {
  id: string;
  type: TType;
  order: number;
  isActive: boolean;
  tenantId?: string | null;
  createdAt?: string;
  updatedAt?: string;
  data: TData;
};

export type HomepageSectionRecord =
  | HomepageSectionRecordBase<"hero", HeroSectionData>
  | HomepageSectionRecordBase<"products", ProductsSectionData>
  | HomepageSectionRecordBase<"banner", BannerSectionData>
  | HomepageSectionRecordBase<"categories", CategoriesSectionData>;

export type ResolvedProductsSectionData = Omit<ProductsSectionData, "productIds" | "source" | "maxItems"> & {
  source: HomepageCollectionSource;
  maxItems: number;
  items: CommerceHubProduct[];
  status: CollectionStatus;
  errorMessage?: string;
};

export type ResolvedCategoriesSectionData = Omit<CategoriesSectionData, "categoryIds" | "source" | "maxItems"> & {
  source: HomepageCollectionSource;
  maxItems: number;
  items: CommerceHubCategory[];
  status: CollectionStatus;
  errorMessage?: string;
};

export type ResolvedHomepageSection =
  | { id: string; type: "hero"; data: HeroSectionData }
  | { id: string; type: "products"; data: ResolvedProductsSectionData }
  | { id: string; type: "banner"; data: BannerSectionData }
  | { id: string; type: "categories"; data: ResolvedCategoriesSectionData };
