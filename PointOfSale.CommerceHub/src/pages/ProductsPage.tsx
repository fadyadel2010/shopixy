import { useInfiniteQuery, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Filter, PackageSearch, Search, Sparkles, TrendingUp } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";

import { ProductCard } from "@/commerce/components/ProductCard";
import { ProductGrid } from "@/commerce/components/ProductGrid";
import { ProductGridSkeleton } from "@/commerce/components/ProductGridSkeleton";
import { FilterChips } from "@/features/discovery/components/FilterChips";
import { FiltersSidebar } from "@/features/discovery/components/FiltersSidebar";
import { MobileFiltersDrawer } from "@/features/discovery/components/MobileFiltersDrawer";
import { ProductSortSelect } from "@/features/discovery/components/ProductSortSelect";
import { formatFilterLabel, formatRangeLabel } from "@/features/discovery/filter-formatting";
import { useProductsScrollRestoration } from "@/features/discovery/hooks/useProductsScrollRestoration";
import {
  buildSearchQueryFromFilters,
  normalizeDiscoveryAttributeKey,
  useFiltersFromUrl
} from "@/features/discovery/hooks/useFiltersFromUrl";
import { useUpdateFilters } from "@/features/discovery/hooks/useUpdateFilters";
import type { DiscoveryAttributeFilter, DiscoveryFiltersState } from "@/features/discovery/types";
import {
  categoryTreeQueryOptions,
  infiniteSearchResultsQueryOptions,
  searchResultsPageQueryOptions
} from "@/features/discovery/queries";
import { analyticsService } from "@/services";
import { EmptyState } from "@/shared/EmptyState";
import { InlineErrorState } from "@/shared/InlineErrorState";
import { SectionHeading } from "@/shared/SectionHeading";
import type { CommerceHubSearchFacets } from "@/types";
import { Button } from "@/ui/button";
import { buildCategoryOptions, formatCurrency, useDocumentTitle, useInfiniteScroll } from "@/utils";

const PAGE_SIZE = 12;

const EMPTY_FACETS: CommerceHubSearchFacets = {
  categories: [],
  price: {},
  attributes: {}
};

type FilterChip = {
  id: string;
  label: string;
  onRemove: () => void;
};

const getAttributeLabel = (key: string, facets: CommerceHubSearchFacets) =>
  facets.attributes[key]?.label ?? formatFilterLabel(key);

const buildAttributeFilterChips = (
  filters: DiscoveryFiltersState,
  facets: CommerceHubSearchFacets,
  setAttributeFilter: (
    key: string,
    filter?: DiscoveryAttributeFilter,
    options?: { replace?: boolean; debounced?: boolean }
  ) => void
) =>
  Object.entries(filters.attributes).flatMap<FilterChip>(([key, filter]) => {
    const label = getAttributeLabel(key, facets);

    if (filter.type === "terms") {
      return filter.values.map((value) => ({
        id: `${key}:${value}`,
        label: `${label}: ${formatFilterLabel(value)}`,
        onRemove: () => {
          const nextValues = filter.values.filter((candidate) => candidate !== value);
          setAttributeFilter(
            key,
            nextValues.length > 0
              ? {
                  type: "terms",
                  values: nextValues
                }
              : undefined
          );
        }
      }));
    }

    const rangeLabel = formatRangeLabel(filter.min, filter.max);
    if (!rangeLabel) {
      return [];
    }

    return [
      {
        id: `${key}:range`,
        label: `${label}: ${rangeLabel}`,
        onRemove: () => setAttributeFilter(key, undefined)
      }
    ];
  });

export const ProductsPage = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [showFilters, setShowFilters] = useState(false);
  const [allowedAttributeKeys, setAllowedAttributeKeys] = useState<ReadonlySet<string>>(() => new Set());

  const filters = useFiltersFromUrl(allowedAttributeKeys);
  const { applyFilters, clearAllFilters, patchFilters, setAttributeFilter } = useUpdateFilters(allowedAttributeKeys);

  const restoreKey = `${location.pathname}${location.search}`;
  const searchQuery = useMemo(
    () => buildSearchQueryFromFilters(filters, 1, PAGE_SIZE),
    [filters]
  );

  const categoriesQuery = useQuery(categoryTreeQueryOptions());
  const categoryOptions = useMemo(() => buildCategoryOptions(categoriesQuery.data ?? []), [categoriesQuery.data]);
  const selectedCategory = useMemo(
    () => categoryOptions.find((category) => category.id === filters.categoryId),
    [categoryOptions, filters.categoryId]
  );

  const searchResultsQuery = useInfiniteQuery(
    infiniteSearchResultsQueryOptions(queryClient, searchQuery, PAGE_SIZE)
  );
  const pages = searchResultsQuery.data?.pages ?? [];
  const firstPage = pages[0];
  const facets = firstPage?.facets ?? EMPTY_FACETS;
  const zeroResultSuggestions = firstPage?.zeroResultSuggestions ?? null;
  const allProducts = useMemo(
    () => pages.flatMap((page) => page.items),
    [pages]
  );
  const totalCount = firstPage?.total ?? 0;
  const nextPage = pages.length + 1;

  const filterChips = useMemo<FilterChip[]>(() => {
    const chips: FilterChip[] = [];

    if (selectedCategory) {
      chips.push({
        id: "category",
        label: selectedCategory.name,
        onRemove: () => patchFilters({ categoryId: "" })
      });
    }

    const priceLabel = formatRangeLabel(filters.priceMin, filters.priceMax);
    if (priceLabel) {
      chips.push({
        id: "price",
        label: `Price: ${priceLabel}`,
        onRemove: () => patchFilters({ priceMin: undefined, priceMax: undefined })
      });
    }

    if (filters.inStock) {
      chips.push({
        id: "stock",
        label: "In stock",
        onRemove: () => patchFilters({ inStock: false })
      });
    }

    chips.push(...buildAttributeFilterChips(filters, facets, setAttributeFilter));
    return chips;
  }, [facets, filters, patchFilters, selectedCategory, setAttributeFilter]);

  const hasActiveFilters = filterChips.length > 0;
  const activeFilterCount = filterChips.length;

  const pageTitle = filters.search
    ? `Search: ${filters.search}`
    : selectedCategory?.name
      ? selectedCategory.name
      : "Products";

  useDocumentTitle(pageTitle);

  const querySignature = location.search;
  const lastQuerySignatureRef = useRef(querySignature);
  const prefetchedPageSignatureRef = useRef<string | null>(null);

  useEffect(() => {
    const facetKeys = Object.keys(firstPage?.facets.attributes ?? {});
    if (facetKeys.length === 0) {
      return;
    }

    setAllowedAttributeKeys((current) => {
      const next = new Set(current);
      let changed = false;

      facetKeys.forEach((key) => {
        const normalizedKey = normalizeDiscoveryAttributeKey(key);
        if (normalizedKey && !next.has(normalizedKey)) {
          next.add(normalizedKey);
          changed = true;
        }
      });

      return changed ? next : current;
    });
  }, [firstPage?.facets.attributes]);

  useEffect(() => {
    if (lastQuerySignatureRef.current === querySignature) {
      return;
    }

    lastQuerySignatureRef.current = querySignature;
    prefetchedPageSignatureRef.current = null;
    setShowFilters(false);
  }, [querySignature]);

  const saveScrollPosition = useProductsScrollRestoration({
    restoreKey,
    itemCount: allProducts.length,
    hasNextPage: Boolean(searchResultsQuery.hasNextPage),
    isFetchingNextPage: searchResultsQuery.isFetchingNextPage,
    fetchNextPage: () => searchResultsQuery.fetchNextPage()
  });

  const prefetchNextPage = useCallback(() => {
    if (!searchResultsQuery.hasNextPage || searchResultsQuery.isFetchingNextPage) {
      return;
    }

    const prefetchSignature = `${querySignature}:${nextPage}`;
    if (prefetchedPageSignatureRef.current === prefetchSignature) {
      return;
    }

    prefetchedPageSignatureRef.current = prefetchSignature;
    void queryClient.prefetchQuery(searchResultsPageQueryOptions(searchQuery, nextPage, PAGE_SIZE));
  }, [
    nextPage,
    queryClient,
    querySignature,
    searchQuery,
    searchResultsQuery.hasNextPage,
    searchResultsQuery.isFetchingNextPage
  ]);

  const prefetchSentinelRef = useInfiniteScroll({
    enabled: Boolean(searchResultsQuery.hasNextPage) && !searchResultsQuery.isFetchingNextPage,
    onIntersect: prefetchNextPage,
    rootMargin: "960px"
  });

  const sentinelRef = useInfiniteScroll({
    enabled: Boolean(searchResultsQuery.hasNextPage) && !searchResultsQuery.isFetchingNextPage,
    onIntersect: () => void searchResultsQuery.fetchNextPage()
  });

  const handleCategoryChange = (categoryId: string) => {
    if (categoryId) {
      const category = categoryOptions.find((item) => item.id === categoryId);
      if (category) {
        analyticsService.trackCategorySelected(category.id, category.name, "products_filter");
      }
    }

    patchFilters({ categoryId });
  };

  const handleAttributeTermsChange = (key: string, values: string[]) => {
    setAttributeFilter(
      key,
      values.length > 0
        ? {
            type: "terms",
            values
          }
        : undefined,
      { replace: true }
    );
  };

  const openSuggestedSearch = (value: string) => {
    patchFilters({ search: value });
  };

  const openSuggestedCategory = (categoryId: string) => {
    applyFilters({
      ...filters,
      search: "",
      categoryId,
      inStock: false,
      priceMin: undefined,
      priceMax: undefined,
      attributes: {}
    });
  };

  return (
    <div className="space-y-8">
      <div className="surface-panel px-6 py-8 md:px-8">
        <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
          <SectionHeading
            eyebrow="Discovery"
            title={filters.search ? `Results for "${filters.search}"` : selectedCategory?.label ?? "Explore products"}
            description={
              filters.search
                ? "Dynamic Shopixy search keeps products, facets, and empty-state recovery in sync with the current URL."
                : "Browse live marketplace listings with backend-driven filters, dynamic facets, and seamless infinite scrolling."
            }
          />
          <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
            <span>{allProducts.length} loaded</span>
            <span className="hidden h-1 w-1 rounded-full bg-border md:block" />
            <span>{totalCount} total results</span>
            {activeFilterCount > 0 ? (
              <>
                <span className="hidden h-1 w-1 rounded-full bg-border md:block" />
                <span>{activeFilterCount} filters active</span>
              </>
            ) : null}
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <aside className="space-y-4 lg:sticky lg:top-24 lg:self-start">
          <Button
            type="button"
            variant="outline"
            className="w-full justify-center gap-2 lg:hidden"
            onClick={() => setShowFilters(true)}
          >
            <Filter className="h-4 w-4" />
            {activeFilterCount > 0 ? `Filters (${activeFilterCount})` : "Show filters"}
          </Button>

          <div className="hidden lg:block">
            <div className="surface-muted p-5 lg:max-h-[calc(100vh-7rem)] lg:overflow-y-auto">
              <FiltersSidebar
                facets={facets}
                filters={filters}
                onCategoryChange={handleCategoryChange}
                onInStockChange={(inStock) => patchFilters({ inStock }, { replace: true })}
                onPriceChange={(range) =>
                  patchFilters(
                    {
                      priceMin: range.min,
                      priceMax: range.max
                    },
                    { replace: true, debounced: true }
                  )
                }
                onAttributeTermsChange={handleAttributeTermsChange}
                onAttributeRangeChange={(key, range) =>
                  setAttributeFilter(
                    key,
                    range.min !== undefined || range.max !== undefined
                      ? {
                          type: "range",
                          min: range.min,
                          max: range.max
                        }
                      : undefined,
                    { replace: true, debounced: true }
                  )
                }
                onClear={clearAllFilters}
              />
            </div>
          </div>
        </aside>

        <section className="space-y-6">
          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
            <FilterChips chips={filterChips} onClearAll={hasActiveFilters ? clearAllFilters : undefined} />
            <ProductSortSelect
              value={filters.sort}
              onChange={(sort) => patchFilters({ sort })}
            />
          </div>

          {searchResultsQuery.isPending ? <ProductGridSkeleton count={8} /> : null}

          {searchResultsQuery.isError ? (
            <InlineErrorState
              title="Products unavailable"
              message={searchResultsQuery.error.message}
              onRetry={() => void searchResultsQuery.refetch()}
            />
          ) : null}

          {!searchResultsQuery.isPending && !searchResultsQuery.isError && allProducts.length === 0 ? (
            <EmptyState
              icon={<PackageSearch className="h-6 w-6" />}
              title={filters.search ? `No results for "${filters.search}"` : "No products found"}
              description={
                hasActiveFilters
                  ? "These filters currently narrow the results too much. Try removing one or more to widen the catalog."
                  : "There are no published Shopixy products available right now."
              }
              actionLabel={hasActiveFilters ? "Clear filters" : undefined}
              onAction={hasActiveFilters ? clearAllFilters : undefined}
            >
              {zeroResultSuggestions ? (
                <div className="space-y-4 text-left">
                  {(zeroResultSuggestions.similarTerms.length > 0 || zeroResultSuggestions.popularTerms.length > 0) ? (
                    <div className="rounded-3xl border border-border/70 bg-white/90 p-4">
                      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                        <Sparkles className="h-4 w-4 text-primary" />
                        Did you mean
                      </div>
                      <div className="mt-3 flex flex-wrap gap-2">
                        {zeroResultSuggestions.similarTerms.map((term) => (
                          <Button key={`similar-${term}`} type="button" variant="outline" size="sm" onClick={() => openSuggestedSearch(term)}>
                            {term}
                          </Button>
                        ))}
                        {zeroResultSuggestions.popularTerms.map((term) => (
                          <Button
                            key={`popular-${term}`}
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="gap-2 rounded-full bg-muted"
                            onClick={() => openSuggestedSearch(term)}
                          >
                            <TrendingUp className="h-3.5 w-3.5" />
                            {term}
                          </Button>
                        ))}
                      </div>
                    </div>
                  ) : null}

                  {zeroResultSuggestions.categories.length > 0 ? (
                    <div className="rounded-3xl border border-border/70 bg-white/90 p-4">
                      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                        <Search className="h-4 w-4 text-primary" />
                        Explore related categories
                      </div>
                      <div className="mt-3 grid gap-2 md:grid-cols-2">
                        {zeroResultSuggestions.categories.map((category) => (
                          <button
                            key={category.categoryId}
                            type="button"
                            className="flex items-center justify-between rounded-2xl border border-transparent bg-muted/60 px-3 py-3 text-left transition-colors hover:border-primary/20 hover:bg-primary/5"
                            onClick={() => openSuggestedCategory(category.categoryId)}
                          >
                            <div>
                              <p className="text-sm font-semibold text-foreground">{category.categoryName ?? "Category"}</p>
                              <p className="text-xs text-muted-foreground">
                                {category.count} related {category.count === 1 ? "item" : "items"}
                              </p>
                            </div>
                            <ArrowRight className="h-4 w-4 text-muted-foreground" />
                          </button>
                        ))}
                      </div>
                    </div>
                  ) : null}

                  {zeroResultSuggestions.products.length > 0 ? (
                    <div className="rounded-3xl border border-border/70 bg-white/90 p-4">
                      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                        <PackageSearch className="h-4 w-4 text-primary" />
                        Top matching products
                      </div>
                      <div className="mt-3 grid gap-3 md:grid-cols-2">
                        {zeroResultSuggestions.products.map((product) => (
                          <button
                            key={product.id}
                            type="button"
                            className="flex items-center gap-3 rounded-2xl border border-transparent bg-muted/60 px-3 py-3 text-left transition-colors hover:border-primary/20 hover:bg-primary/5"
                            onClick={() => {
                              saveScrollPosition();
                              analyticsService.trackProductClicked(product.productId, product.variantId, "zero_results_recovery");
                              navigate(`/product/${product.id}`);
                            }}
                          >
                            <div className="flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded-2xl bg-background">
                              {product.image ? (
                                <img src={product.image} alt="" className="h-full w-full object-cover" loading="lazy" />
                              ) : (
                                <PackageSearch className="h-5 w-5 text-muted-foreground" />
                              )}
                            </div>

                            <div className="min-w-0 flex-1">
                              <p className="line-clamp-2 text-sm font-semibold text-foreground">{product.productName}</p>
                              <p className="mt-1 text-xs text-muted-foreground">
                                {[product.categoryName, product.storeName].filter(Boolean).join(" / ")}
                              </p>
                            </div>

                            <div className="shrink-0 text-right">
                              <p className="text-sm font-extrabold text-foreground">
                                {formatCurrency(product.price, product.currency)}
                              </p>
                              <ArrowRight className="ml-auto mt-2 h-4 w-4 text-muted-foreground" />
                            </div>
                          </button>
                        ))}
                      </div>
                    </div>
                  ) : null}
                </div>
              ) : null}
            </EmptyState>
          ) : null}

          {!searchResultsQuery.isPending && !searchResultsQuery.isError && allProducts.length > 0 ? (
            <>
              <ProductGrid>
                {allProducts.map((product) => (
                  <ProductCard
                    key={product.id}
                    product={product}
                    onClick={(clickedProduct) => {
                      saveScrollPosition();
                      analyticsService.trackProductClicked(clickedProduct.productId, clickedProduct.variantId, "products_grid");
                    }}
                  />
                ))}
              </ProductGrid>

              <div className="min-h-12">
                {searchResultsQuery.hasNextPage ? (
                  <div className="space-y-3">
                    <div ref={prefetchSentinelRef} className="h-1 w-full" aria-hidden="true" />
                    <div ref={sentinelRef} className="h-4 w-full" />
                  </div>
                ) : null}

                {searchResultsQuery.isFetchingNextPage ? (
                  <div className="flex items-center justify-center gap-3 rounded-2xl border border-border/70 bg-white/80 px-4 py-3 text-sm text-muted-foreground">
                    <div className="skeleton-wave h-2 w-16 rounded-full" aria-hidden="true" />
                    Loading more products
                  </div>
                ) : null}
              </div>
            </>
          ) : null}

          {!searchResultsQuery.isPending && !searchResultsQuery.isError ? (
            <div className="rounded-2xl border border-border/70 bg-white/80 px-4 py-3 text-sm text-muted-foreground">
              <div className="flex items-center gap-2">
                <Search className="h-4 w-4" />
                Filter state lives in the URL, so refreshes and shared links restore the same search, sort, and facet combination.
              </div>
            </div>
          ) : null}
        </section>
      </div>

      <MobileFiltersDrawer
        open={showFilters}
        facets={facets}
        filters={filters}
        onClose={() => setShowFilters(false)}
        onApply={(nextFilters) => applyFilters(nextFilters)}
      />
    </div>
  );
};
