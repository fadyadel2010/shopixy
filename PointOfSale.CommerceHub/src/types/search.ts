import type { CommerceHubProductSort, CommerceHubProduct } from "./product";

export type CommerceHubSearchIntent = "product" | "category" | "price" | "unknown";

export type CommerceHubAutocompleteProduct = {
  id: string;
  productId: string;
  variantId: string;
  productName: string;
  productNameHighlight?: string | null;
  image?: string | null;
  price: number;
  currency: string;
  storeName: string;
  categoryId: string;
  categoryName?: string | null;
  categoryNameHighlight?: string | null;
};

export type CommerceHubAutocompleteCategory = {
  categoryId: string;
  categoryName: string;
  slug: string;
  count: number;
};

export type CommerceHubAutocompleteQuery = {
  q?: string;
  categoryId?: string;
  tenantId?: string;
  productLimit?: number;
  categoryLimit?: number;
  suggestionLimit?: number;
  popularLimit?: number;
};

export type CommerceHubAutocompleteResponse = {
  query: string;
  intent: CommerceHubSearchIntent;
  products: CommerceHubAutocompleteProduct[];
  categories: CommerceHubAutocompleteCategory[];
  suggestions: string[];
  popular: string[];
};

export type CommerceHubPopularSearchesQuery = {
  days?: number;
  limit?: number;
};

export type CommerceHubPopularSearchQuery = {
  query: string;
  searchCount: number;
  zeroResultCount: number;
  clickCount: number;
  clickThroughRate: number;
};

export type CommerceHubPopularSearchesResponse = {
  days: number;
  items: CommerceHubPopularSearchQuery[];
};

export type CommerceHubSearchQuery = {
  q?: string;
  categoryId?: string;
  tenantId?: string;
  inStock?: boolean;
  minPrice?: number;
  maxPrice?: number;
  sort?: CommerceHubProductSort;
  page?: number;
  pageSize?: number;
  filters?: string;
};

export type CommerceHubAggregationBucket = {
  value: string;
  count: number;
};

export type CommerceHubAggregationStats = {
  min?: number | null;
  max?: number | null;
  avg?: number | null;
  count: number;
};

export type CommerceHubAggregation = {
  type: string;
  values: CommerceHubAggregationBucket[];
  stats?: CommerceHubAggregationStats | null;
};

export type CommerceHubFacetRange = {
  min?: number | null;
  max?: number | null;
};

export type CommerceHubSearchFacetCategory = {
  categoryId: string;
  name: string;
  slug: string;
  count: number;
  children: CommerceHubSearchFacetCategory[];
};

export type CommerceHubAttributeFacet = {
  key: string;
  label: string;
  type: string;
  sortOrder: number;
  valueCount: number;
  values: CommerceHubAggregationBucket[];
  range?: CommerceHubFacetRange | null;
};

export type CommerceHubSearchFacets = {
  categories: CommerceHubSearchFacetCategory[];
  price: CommerceHubFacetRange;
  attributes: Record<string, CommerceHubAttributeFacet>;
};

export type CommerceHubSearchSuggestionProduct = {
  id: string;
  productId: string;
  variantId: string;
  productName: string;
  image?: string | null;
  price: number;
  currency: string;
  categoryId: string;
  categoryName?: string | null;
  storeName: string;
};

export type CommerceHubSearchCategorySuggestion = {
  categoryId: string;
  categoryName?: string | null;
  count: number;
};

export type CommerceHubZeroResultSuggestions = {
  products: CommerceHubSearchSuggestionProduct[];
  popularTerms: string[];
  similarTerms: string[];
  categories: CommerceHubSearchCategorySuggestion[];
  filters: Record<string, CommerceHubAggregation>;
};

export type CommerceHubSearchResponse = {
  items: CommerceHubProduct[];
  total: number;
  page: number;
  pageSize: number;
  intent: CommerceHubSearchIntent;
  facets: CommerceHubSearchFacets;
  aggregations: Record<string, CommerceHubAggregation>;
  zeroResultSuggestions?: CommerceHubZeroResultSuggestions | null;
};

export type CommerceHubSearchClickRequest = {
  query?: string;
  productId: string;
  variantId?: string;
  position: number;
  sessionId?: string;
  tenantId?: string;
};

export type CommerceHubSearchImpressionRequest = {
  query?: string;
  productIds: string[];
  sessionId?: string;
  timestamp?: string;
  tenantId?: string;
};

export type CommerceHubSearchFilterEventRequest = {
  filterKey: string;
  filterValue: string;
  query?: string;
  sessionId?: string;
  timestamp?: string;
  tenantId?: string;
};
