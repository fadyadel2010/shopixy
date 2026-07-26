import type { CommerceHubProductSort } from "@/types";

export const DISCOVERY_SCROLL_STORAGE_KEY = "commercehub:products-scroll";

export const PRODUCT_SORT_OPTIONS: Array<{ value: CommerceHubProductSort; label: string }> = [
  { value: "relevance", label: "Relevance" },
  { value: "price_asc", label: "Price Low to High" },
  { value: "price_desc", label: "Price High to Low" },
  { value: "newest", label: "Newest Products" }
];

export const isCommerceHubProductSort = (value: string | null): value is CommerceHubProductSort =>
  PRODUCT_SORT_OPTIONS.some((option) => option.value === value);
