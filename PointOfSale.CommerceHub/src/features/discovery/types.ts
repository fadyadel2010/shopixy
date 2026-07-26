import type { CommerceHubProductSort } from "@/types";

export type DiscoveryAttributeTermFilter = {
  type: "terms";
  values: string[];
};

export type DiscoveryAttributeRangeFilter = {
  type: "range";
  min?: number;
  max?: number;
};

export type DiscoveryAttributeFilter =
  | DiscoveryAttributeTermFilter
  | DiscoveryAttributeRangeFilter;

export type DiscoveryFiltersState = {
  categoryId: string;
  search: string;
  inStock: boolean;
  sort: CommerceHubProductSort | "";
  priceMin?: number;
  priceMax?: number;
  attributes: Record<string, DiscoveryAttributeFilter>;
};
